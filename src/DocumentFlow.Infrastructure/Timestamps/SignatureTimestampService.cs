using System.Security.Cryptography;
using DocumentFlow.Application.Timestamps;
using DocumentFlow.Application.Documents;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using DocumentFlow.Infrastructure.Signatures;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DocumentFlow.Infrastructure.Timestamps;

public sealed class SignatureTimestampService(DocumentFlowDbContext db, ITimestampProvider provider,
    IRsaSigningKeyStore signingKeys, IFileStorage storage) : ISignatureTimestampService
{
    public async Task<TimestampCreateResponse> CreateAsync(Guid signatureId, Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var record = await LoadAsync(signatureId, userId, isAdmin, cancellationToken);
        if (record is null) throw new TimestampAccessException("Signature not found or document access denied.");
        if (!isAdmin && (record.SignedByUserId != userId || !record.SignerIsActive || record.SignerRole != UserRole.Manager))
            throw new TimestampAccessException("Only the signing Manager or an administrator can request a timestamp.");
        if (!await IsSignatureValidAsync(record, cancellationToken)) throw new TimestampStateException("Only a cryptographically valid signature on an intact document can be timestamped.");

        var existing = await db.DocumentTimestamps.AsNoTracking().SingleOrDefaultAsync(x => x.DocumentSignatureId == signatureId, cancellationToken);
        if (existing is not null) return new(ToSummary(existing), await VerifyExistingAsync(existing, record, cancellationToken));

        var nonce = RandomNumberGenerator.GetBytes(20);
        var issued = await provider.IssueAsync(Convert.FromBase64String(record.SignatureValue), nonce, cancellationToken);
        var verification = provider.Verify(issued.Token, Convert.FromBase64String(record.SignatureValue), nonce);
        if (verification.Result != "Valid") throw new TimestampProviderUnavailableException("Timestamp token validation failed before persistence.");
        if (!await IsSignatureValidAsync(record, cancellationToken)) throw new TimestampStateException("Document integrity changed while the timestamp was being issued.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var timestamp = new DocumentTimestamp(Guid.NewGuid(), signatureId, Convert.ToBase64String(issued.Token), issued.Authority,
            issued.SerialNumber, issued.Algorithm, issued.Imprint, issued.GenTimeUtc, now, issued.ProviderType, Convert.ToHexString(nonce));
        db.DocumentTimestamps.Add(timestamp);
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), userId, AuditAction.TimestampCreated, record.DocumentId,
            record.DocumentVersionId, null, $"{issued.ProviderType} TSA tarafından dijital imzaya zaman damgası eklendi.", now));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            var concurrent = await db.DocumentTimestamps.AsNoTracking().SingleOrDefaultAsync(x => x.DocumentSignatureId == signatureId, cancellationToken);
            if (concurrent is not null) return new(ToSummary(concurrent), await VerifyExistingAsync(concurrent, record, cancellationToken));
            throw;
        }
        return new(ToSummary(timestamp), verification);
    }

    public async Task<IReadOnlyList<TimestampSummary>?> ListAsync(Guid signatureId, Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var record = await LoadAsync(signatureId, userId, isAdmin, cancellationToken);
        if (record is null) return null;
        var timestamps = await db.DocumentTimestamps.AsNoTracking().Where(x => x.DocumentSignatureId == signatureId)
            .OrderByDescending(x => x.GenTimeUtc).Select(x => new TimestampSummary(x.Id, x.DocumentSignatureId,
                x.TimestampAuthority, x.SerialNumber, x.MessageImprintAlgorithm, x.MessageImprint, x.GenTimeUtc,
                x.CreatedAtUtc, x.ProviderType)).ToListAsync(cancellationToken);
        return timestamps;
    }

    public async Task<TimestampVerification?> VerifyAsync(Guid signatureId, Guid timestampId, Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var record = await LoadAsync(signatureId, userId, isAdmin, cancellationToken);
        if (record is null) return null;
        var timestamp = await db.DocumentTimestamps.AsNoTracking().SingleOrDefaultAsync(x => x.Id == timestampId && x.DocumentSignatureId == signatureId, cancellationToken);
        return timestamp is null ? null : await VerifyExistingAsync(timestamp, record, cancellationToken);
    }

    private async Task<SignatureData?> LoadAsync(Guid signatureId, Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var record = await db.DocumentSignatures.AsNoTracking().Where(x => x.Id == signatureId)
            .Select(x => new SignatureData(x.Id, x.DocumentId, x.DocumentVersionId, x.SignedByUserId, x.SignedByUser.Role, x.SignedByUser.IsActive, x.SignatureValue,
                x.DocumentHash, x.PublicKeyFingerprint, x.DocumentVersion.VersionNumber, x.DocumentVersion.StoredFileName,
                x.DocumentVersion.FileHash, x.SignedByUser.FirstName + " " + x.SignedByUser.LastName)).SingleOrDefaultAsync(cancellationToken);
        if (record is null) return null;
        var allowed = isAdmin || record.SignedByUserId == userId ||
            await db.Documents.AnyAsync(x => x.Id == record.DocumentId && x.OwnerId == userId, cancellationToken) ||
            await db.ApprovalRequests.AnyAsync(x => x.DocumentId == record.DocumentId && x.DocumentVersionId == record.DocumentVersionId && x.AssignedToUserId == userId, cancellationToken);
        return allowed ? record : null;
    }

    private async Task<TimestampVerification> VerifyExistingAsync(DocumentTimestamp timestamp, SignatureData record, CancellationToken cancellationToken)
    {
        if (!await IsSignatureValidAsync(record, cancellationToken)) return new("Invalid", "Document integrity or digital signature validation failed.", timestamp.TimestampAuthority, timestamp.SerialNumber, timestamp.MessageImprintAlgorithm, timestamp.MessageImprint, timestamp.GenTimeUtc);
        try
        {
            var result = provider.Verify(Convert.FromBase64String(timestamp.TimestampToken), Convert.FromBase64String(record.SignatureValue), Convert.FromHexString(timestamp.Nonce));
            if (result.Result == "Valid" && (result.SerialNumber != timestamp.SerialNumber || result.Algorithm != timestamp.MessageImprintAlgorithm ||
                result.Imprint != timestamp.MessageImprint || result.GenTimeUtc?.Ticks / 10 != timestamp.GenTimeUtc.Ticks / 10))
                return new("Invalid", "Stored timestamp metadata does not match the signed RFC 3161 token.", timestamp.TimestampAuthority,
                    timestamp.SerialNumber, timestamp.MessageImprintAlgorithm, timestamp.MessageImprint, timestamp.GenTimeUtc);
            return result;
        }
        catch (FormatException) { return new("Invalid", "Stored timestamp token metadata is malformed.", timestamp.TimestampAuthority, timestamp.SerialNumber, timestamp.MessageImprintAlgorithm, timestamp.MessageImprint, timestamp.GenTimeUtc); }
    }

    private async Task<bool> IsSignatureValidAsync(SignatureData record, CancellationToken cancellationToken)
    {
        using var key = signingKeys.GetTrustedPublicKey(record.PublicKeyFingerprint);
        if (key is null) return false;
        try
        {
            await using var stream = await storage.OpenReadAsync(record.DocumentId, record.VersionNumber, record.StoredFileName, cancellationToken);
            if (stream is null) return false;
            var currentHash = await SHA256.HashDataAsync(stream, cancellationToken);
            if (!FixedHexEquals(Convert.ToHexString(currentHash), record.FileHash) || !FixedHexEquals(Convert.ToHexString(currentHash), record.DocumentHash)) return false;
            return key.VerifyHash(currentHash, Convert.FromBase64String(record.SignatureValue), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException) { return false; }
    }

    private static bool FixedHexEquals(string left, string right)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right)); }
        catch (FormatException) { return false; }
    }

    private static TimestampSummary ToSummary(DocumentTimestamp x) => new(x.Id, x.DocumentSignatureId,
        x.TimestampAuthority, x.SerialNumber, x.MessageImprintAlgorithm, x.MessageImprint, x.GenTimeUtc,
        x.CreatedAtUtc, x.ProviderType);

    private sealed record SignatureData(Guid Id, Guid DocumentId, Guid DocumentVersionId, Guid SignedByUserId,
        UserRole SignerRole, bool SignerIsActive, string SignatureValue, string DocumentHash, string PublicKeyFingerprint, int VersionNumber,
        string StoredFileName, string FileHash, string SignerName);
}
