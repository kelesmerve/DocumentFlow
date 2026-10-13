using System.Security.Cryptography;
using DocumentFlow.Application.Signatures;
using DocumentFlow.Application.Documents;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DocumentFlow.Infrastructure.Signatures;

public sealed class DocumentSignatureService(DocumentFlowDbContext db, IFileStorage storage, IRsaSigningKeyStore keys) : IDocumentSignatureService
{
    public async Task<SignatureSummary> SignAsync(Guid documentId, Guid versionId, Guid userId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await SignCoreAsync(documentId, versionId, userId, null, SignatureMethod.Direct, true, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            var concurrent = await FindSummaryAsync(documentId, versionId, userId, cancellationToken);
            if (concurrent is not null) return concurrent;
            throw;
        }
    }

    public Task<SignatureSummary> SignForMobileAsync(Guid documentId, Guid versionId, Guid userId, string expectedHash, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Mobile signing requires a database transaction.");
        return SignCoreAsync(documentId, versionId, userId, expectedHash, SignatureMethod.MobileSimulation, false, cancellationToken);
    }

    private async Task<SignatureSummary> SignCoreAsync(Guid documentId, Guid versionId, Guid userId, string? expectedHash,
        SignatureMethod method, bool returnExisting, CancellationToken cancellationToken)
    {
        var signer = await db.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new { x.Id, x.FirstName, x.LastName, x.Role, x.IsActive }).SingleOrDefaultAsync(cancellationToken);
        if (signer is null || !signer.IsActive || signer.Role != UserRole.Manager)
            throw new SignatureAccessException("Yalnızca aktif yöneticiler belge imzalayabilir.");

        var version = await db.DocumentVersions.AsNoTracking()
            .Where(x => x.Id == versionId && x.DocumentId == documentId)
            .Select(x => new { x.Id, x.DocumentId, x.VersionNumber, x.StoredFileName, x.FileHash, DocumentStatus = x.Document.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (version is null) throw new SignatureAccessException("Belge versiyonu bulunamadı.");

        var assignedApproval = await db.ApprovalRequests.AsNoTracking().AnyAsync(x =>
            x.DocumentId == documentId && x.DocumentVersionId == versionId && x.AssignedToUserId == userId && x.Status == ApprovalStatus.Approved,
            cancellationToken);
        if (!assignedApproval) throw new SignatureAccessException("Bu versiyon size atanmış onaylı bir belge değil.");
        if (version.DocumentStatus != DocumentStatus.Approved)
            throw new SignatureStateException("Yalnızca onaylı belgeler imzalanabilir.");

        await using var file = await storage.OpenReadAsync(documentId, version.VersionNumber, version.StoredFileName, cancellationToken);
        if (file is null) throw new SignatureIntegrityException("Belge dosyası bulunamadı; imza oluşturulmadı.");
        var hash = await SHA256.HashDataAsync(file, cancellationToken);
        var hashHex = Convert.ToHexString(hash);
        if (!FixedHexEquals(hashHex, version.FileHash)) throw new SignatureIntegrityException("Dosya içeriği kayıtlı SHA-256 değeriyle eşleşmiyor.");
        if (expectedHash is not null && !FixedHexEquals(hashHex, expectedHash))
            throw new SignatureIntegrityException("Dosya içeriği mobil imza talebindeki SHA-256 değeriyle eşleşmiyor.");

        var existing = await FindSummaryAsync(documentId, versionId, userId, cancellationToken);
        if (existing is not null)
        {
            if (returnExisting) return existing;
            throw new SignatureStateException("Bu versiyon kullanıcı tarafından zaten imzalanmış.");
        }

        var key = keys.GetCurrentSigningKey();
        if (key is null) throw new SignatureProviderUnavailableException("İmza anahtarı yapılandırılmamış; imzalama kullanılamıyor.");
        using var privateKey = key.PrivateKey;
        var signatureBytes = privateKey.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var now = DateTime.UtcNow;
        var signature = new DocumentSignature(Guid.NewGuid(), documentId, versionId, userId,
            "RSA-PSS", "SHA-256", hashHex, Convert.ToBase64String(signatureBytes), key.Fingerprint, now, method);
        db.DocumentSignatures.Add(signature);
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), userId, AuditAction.DocumentSigned, documentId,
            versionId, null, $"v{version.VersionNumber} dijital olarak imzalandı.", now));
        await db.SaveChangesAsync(cancellationToken);
        return ToSummary(signature, signer.FirstName + " " + signer.LastName);
    }

    public async Task<SignatureList?> ListAsync(Guid documentId, Guid versionId, Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        if (!await CanViewAsync(documentId, versionId, userId, isAdmin, cancellationToken)) return null;
        var rows = await db.DocumentSignatures.AsNoTracking().Where(x => x.DocumentId == documentId && x.DocumentVersionId == versionId)
            .OrderBy(x => x.SignedAtUtc).ThenBy(x => x.Id)
            .Select(x => new SignatureSummary(x.Id, x.DocumentId, x.DocumentVersionId, x.SignedByUserId,
                x.SignedByUser.FirstName + " " + x.SignedByUser.LastName, x.SignatureAlgorithm, x.HashAlgorithm,
                x.DocumentHash, x.PublicKeyFingerprint, x.SignedAtUtc, x.Method))
            .ToListAsync(cancellationToken);
        var canSign = await db.Users.AnyAsync(x => x.Id == userId && x.IsActive && x.Role == UserRole.Manager, cancellationToken) &&
            await db.Documents.AnyAsync(x => x.Id == documentId && x.Status == DocumentStatus.Approved, cancellationToken) &&
            await db.ApprovalRequests.AnyAsync(x => x.DocumentId == documentId && x.DocumentVersionId == versionId &&
                x.AssignedToUserId == userId && x.Status == ApprovalStatus.Approved, cancellationToken);
        return new SignatureList(rows, canSign);
    }

    public async Task<IReadOnlyList<SignatureVerification>?> VerifyAsync(Guid documentId, Guid versionId, Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        if (!await CanViewAsync(documentId, versionId, userId, isAdmin, cancellationToken)) return null;
        var version = await db.DocumentVersions.AsNoTracking().Where(x => x.Id == versionId && x.DocumentId == documentId)
            .Select(x => new { x.VersionNumber, x.StoredFileName, x.FileHash }).SingleOrDefaultAsync(cancellationToken);
        if (version is null) return null;

        var signatures = await db.DocumentSignatures.AsNoTracking()
            .Where(x => x.DocumentId == documentId && x.DocumentVersionId == versionId)
            .OrderBy(x => x.SignedAtUtc).ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id, x.SignedByUserId, Signer = x.SignedByUser.FirstName + " " + x.SignedByUser.LastName,
                x.SignatureAlgorithm, x.HashAlgorithm, x.DocumentHash, x.SignatureValue, x.PublicKeyFingerprint, x.SignedAtUtc
            }).ToListAsync(cancellationToken);
        if (signatures.Count == 0) return [];

        byte[]? currentHash = null;
        var fileAvailable = false;
        await using (var stream = await storage.OpenReadAsync(documentId, version.VersionNumber, version.StoredFileName, cancellationToken))
        {
            if (stream is not null)
            {
                fileAvailable = true;
                currentHash = await SHA256.HashDataAsync(stream, cancellationToken);
            }
        }

        var results = new List<SignatureVerification>(signatures.Count);
        foreach (var signature in signatures)
        {
            string result;
            string message;
            if (!fileAvailable || currentHash is null)
            {
                result = "Unverifiable";
                message = "Belge dosyası bulunamadı; imza doğrulanamadı.";
            }
            else if (!FixedHexEquals(Convert.ToHexString(currentHash), version.FileHash) || !FixedHexEquals(Convert.ToHexString(currentHash), signature.DocumentHash))
            {
                result = "Invalid";
                message = "Dosyanın SHA-256 değeri imza sırasında kaydedilen değerle eşleşmiyor.";
            }
            else
            {
                using var publicKey = keys.GetTrustedPublicKey(signature.PublicKeyFingerprint);
                if (publicKey is null)
                {
                    result = "Unverifiable";
                    message = "İmzada kullanılan güvenilir açık anahtar bulunamadı.";
                }
                else
                {
                    var valid = false;
                    try { valid = publicKey.VerifyHash(currentHash, Convert.FromBase64String(signature.SignatureValue), HashAlgorithmName.SHA256, RSASignaturePadding.Pss); }
                    catch (FormatException) { }
                    catch (CryptographicException) { }
                    result = valid ? "Valid" : "Invalid";
                    message = valid ? "Dosya bütünlüğü ve kayıtlı açık anahtarla imza doğrulandı; gerçek kişi kimliği veya hukuki geçerlilik doğrulanmaz." : "İmza verisi doğrulanamadı.";
                }
            }
            results.Add(new SignatureVerification(signature.Id, signature.SignedByUserId, signature.Signer,
                signature.SignatureAlgorithm, signature.HashAlgorithm, signature.PublicKeyFingerprint,
                signature.SignedAtUtc, result, message));
        }
        return results;
    }

    private async Task<bool> CanViewAsync(Guid documentId, Guid versionId, Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        if (!await db.DocumentVersions.AnyAsync(x => x.Id == versionId && x.DocumentId == documentId, cancellationToken)) return false;
        return isAdmin || await db.Documents.AnyAsync(x => x.Id == documentId && x.OwnerId == userId, cancellationToken) ||
            await db.ApprovalRequests.AnyAsync(x => x.DocumentId == documentId && x.DocumentVersionId == versionId && x.AssignedToUserId == userId, cancellationToken);
    }

    private async Task<SignatureSummary?> FindSummaryAsync(Guid documentId, Guid versionId, Guid userId, CancellationToken cancellationToken) =>
        await db.DocumentSignatures.AsNoTracking().Where(x => x.DocumentId == documentId && x.DocumentVersionId == versionId && x.SignedByUserId == userId)
            .Select(x => new SignatureSummary(x.Id, x.DocumentId, x.DocumentVersionId, x.SignedByUserId,
                x.SignedByUser.FirstName + " " + x.SignedByUser.LastName, x.SignatureAlgorithm, x.HashAlgorithm,
                x.DocumentHash, x.PublicKeyFingerprint, x.SignedAtUtc, x.Method)).SingleOrDefaultAsync(cancellationToken);

    private static SignatureSummary ToSummary(DocumentSignature signature, string signerName) => new(
        signature.Id, signature.DocumentId, signature.DocumentVersionId, signature.SignedByUserId, signerName,
        signature.SignatureAlgorithm, signature.HashAlgorithm, signature.DocumentHash,
        signature.PublicKeyFingerprint, signature.SignedAtUtc, signature.Method);

    private static bool FixedHexEquals(string left, string right)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right)); }
        catch (FormatException) { return false; }
    }
}
