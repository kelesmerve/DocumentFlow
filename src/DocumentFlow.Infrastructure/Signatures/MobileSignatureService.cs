using System.Security.Cryptography;
using DocumentFlow.Application.Documents;
using DocumentFlow.Application.Signatures;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using DocumentFlow.Infrastructure.Integrations;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DocumentFlow.Infrastructure.Signatures;

public sealed class MobileSignatureService(DocumentFlowDbContext db, IFileStorage storage,
    IDocumentSignatureService signatures, IPasswordHasher<User> passwordHasher,
    Microsoft.Extensions.Configuration.IConfiguration configuration) : IMobileSignatureService
{
    private TimeSpan Lifetime => TimeSpan.FromMinutes(Math.Clamp(
        int.TryParse(configuration["MobileSignature:RequestLifetimeMinutes"], out var minutes) ? minutes : 5, 1, 60));

    public async Task<MobileSignatureSummary> CreateAsync(Guid documentId, Guid versionId, Guid userId, CancellationToken cancellationToken)
    {
        await RequireManagerAsync(userId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var version = await db.DocumentVersions.AsNoTracking().Where(x => x.Id == versionId && x.DocumentId == documentId)
            .Select(x => new { x.VersionNumber, x.StoredFileName, x.FileHash, x.Document.DocumentNumber,
                x.Document.Title, x.Document.Status }).SingleOrDefaultAsync(cancellationToken);
        if (version is null) throw new MobileSignatureAccessException("Belge versiyonu bulunamadı.");
        if (version.Status != DocumentStatus.Approved) throw new MobileSignatureStateException("Belge onaylı olmalıdır.");
        if (!await db.ApprovalRequests.AnyAsync(x => x.DocumentId == documentId && x.DocumentVersionId == versionId &&
            x.AssignedToUserId == userId && x.Status == ApprovalStatus.Approved, cancellationToken))
            throw new MobileSignatureAccessException("Bu belge versiyonunu onaylayan yönetici değilsiniz.");

        await VerifyFileAsync(documentId, version.VersionNumber, version.StoredFileName, version.FileHash, cancellationToken);
        var now = DateTime.UtcNow;
        await ExpireDueAsync(userId, now, cancellationToken);
        var existing = await Project(Owned(userId).Where(x => x.DocumentVersionId == versionId && x.Status == MobileSignatureStatus.Pending))
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null) { await transaction.CommitAsync(cancellationToken); return existing; }
        if (await db.DocumentSignatures.AnyAsync(x => x.DocumentVersionId == versionId && x.SignedByUserId == userId, cancellationToken))
            throw new MobileSignatureStateException("Bu versiyon zaten imzalanmış.");

        var request = new MobileSignatureRequest(Guid.NewGuid(), documentId, versionId, userId, version.FileHash, now, now.Add(Lifetime));
        db.MobileSignatureRequests.Add(request);
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), userId, AuditAction.MobileSignatureRequested, documentId,
            versionId, null, $"v{version.VersionNumber} için mobil imza simülasyonu talep edildi.", now));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            var concurrent = await Project(Owned(userId).Where(x => x.DocumentVersionId == versionId && x.Status == MobileSignatureStatus.Pending))
                .FirstOrDefaultAsync(cancellationToken);
            if (concurrent is not null) return concurrent;
            throw;
        }
        return new MobileSignatureSummary(request.Id, documentId, versionId, version.DocumentNumber,
            version.Title, version.VersionNumber, request.Status, now, request.ExpiresAtUtc, null, null);
    }

    public async Task<IReadOnlyList<MobileSignatureSummary>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        await RequireManagerAsync(userId, cancellationToken);
        await ExpireDueAsync(userId, DateTime.UtcNow, cancellationToken);
        return await Project(Owned(userId).OrderByDescending(x => x.CreatedAtUtc).Take(100)).ToListAsync(cancellationToken);
    }

    public async Task<MobileSignatureSummary?> GetAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        await RequireManagerAsync(userId, cancellationToken);
        await ExpireDueAsync(userId, DateTime.UtcNow, cancellationToken);
        return await Project(Owned(userId).Where(x => x.Id == id)).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<MobileSignatureSummary> ApproveAsync(Guid id, Guid userId, string password, CancellationToken cancellationToken)
    {
        var user = await RequireManagerAsync(userId, cancellationToken);
        if (string.IsNullOrEmpty(password) || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
            throw new MobileSignaturePasswordException();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var request = await LockOwnedAsync(id, userId, cancellationToken);
        if (request.Status == MobileSignatureStatus.Approved)
        {
            await transaction.CommitAsync(cancellationToken);
            return await GetStoredAsync(id, userId, cancellationToken);
        }
        await EnsurePendingAsync(request, cancellationToken);
        var signature = await signatures.SignForMobileAsync(request.DocumentId, request.DocumentVersionId, userId, request.FileHash, cancellationToken);
        var now = DateTime.UtcNow;
        request.Approve(signature.Id, now);
        db.OutboxMessages.Add(IntegrationEventFactory.Create(IntegrationEventFactory.MobileSignatureCompleted,
            request.Id, request.DocumentId, request.DocumentVersionId, now, signature.Id));
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), userId, AuditAction.MobileSignatureApproved,
            request.DocumentId, request.DocumentVersionId, null, "Mobil imza talebi onaylandı.", now));
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), userId, AuditAction.MobileSignatureCompleted,
            request.DocumentId, request.DocumentVersionId, null, "Mobil imza simülasyonu tamamlandı.", now));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetStoredAsync(id, userId, cancellationToken);
    }

    public async Task<MobileSignatureSummary> RejectAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        await RequireManagerAsync(userId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var request = await LockOwnedAsync(id, userId, cancellationToken);
        if (request.Status == MobileSignatureStatus.Rejected)
        {
            await transaction.CommitAsync(cancellationToken);
            return await GetStoredAsync(id, userId, cancellationToken);
        }
        await EnsurePendingAsync(request, cancellationToken);
        var now = DateTime.UtcNow;
        request.Reject(now);
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), userId, AuditAction.MobileSignatureRejected,
            request.DocumentId, request.DocumentVersionId, null, "Mobil imza talebi reddedildi.", now));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetStoredAsync(id, userId, cancellationToken);
    }

    private async Task<User> RequireManagerAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null || !user.IsActive || user.Role != UserRole.Manager)
            throw new MobileSignatureAccessException("Yalnızca aktif yöneticiler mobil imza işlemi yapabilir.");
        return user;
    }

    private async Task<MobileSignatureRequest> LockOwnedAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var request = await db.MobileSignatureRequests.FromSqlInterpolated(
            $"SELECT * FROM mobile_signature_requests WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        if (request is null || request.ManagerId != userId) throw new MobileSignatureAccessException("Talep bulunamadı.");
        return request;
    }

    private async Task EnsurePendingAsync(MobileSignatureRequest request, CancellationToken cancellationToken)
    {
        if (request.Status != MobileSignatureStatus.Pending) throw new MobileSignatureStateException("Talep artık beklemede değil.");
        var now = DateTime.UtcNow;
        if (request.ExpiresAtUtc > now) return;
        request.Expire(now);
        await db.SaveChangesAsync(cancellationToken);
        await db.Database.CommitTransactionAsync(cancellationToken);
        throw new MobileSignatureStateException("Mobil imza talebinin süresi doldu.");
    }

    private async Task ExpireDueAsync(Guid userId, DateTime now, CancellationToken cancellationToken) =>
        await db.MobileSignatureRequests.Where(x => x.ManagerId == userId && x.Status == MobileSignatureStatus.Pending && x.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(x => x.SetProperty(r => r.Status, MobileSignatureStatus.Expired)
                .SetProperty(r => r.CompletedAtUtc, now), cancellationToken);

    private async Task VerifyFileAsync(Guid documentId, int versionNumber, string storedFileName, string expectedHash, CancellationToken cancellationToken)
    {
        await using var file = await storage.OpenReadAsync(documentId, versionNumber, storedFileName, cancellationToken);
        if (file is null) throw new SignatureIntegrityException("Belge dosyası bulunamadı.");
        var actual = await SHA256.HashDataAsync(file, cancellationToken);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expectedHash)))
                throw new SignatureIntegrityException("Belge dosyasının SHA-256 değeri değişmiş.");
        }
        catch (FormatException) { throw new SignatureIntegrityException("Kayıtlı SHA-256 değeri geçersiz."); }
    }

    private IQueryable<MobileSignatureRequest> Owned(Guid userId) =>
        db.MobileSignatureRequests.AsNoTracking().Where(x => x.ManagerId == userId);

    private static IQueryable<MobileSignatureSummary> Project(IQueryable<MobileSignatureRequest> query) =>
        query.Select(x => new MobileSignatureSummary(x.Id, x.DocumentId, x.DocumentVersionId, x.Document.DocumentNumber,
                x.Document.Title, x.DocumentVersion.VersionNumber, x.Status, x.CreatedAtUtc, x.ExpiresAtUtc,
                x.CompletedAtUtc, x.DocumentSignatureId));

    private async Task<MobileSignatureSummary> GetStoredAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        await Project(Owned(userId).Where(x => x.Id == id)).SingleAsync(cancellationToken);
}
