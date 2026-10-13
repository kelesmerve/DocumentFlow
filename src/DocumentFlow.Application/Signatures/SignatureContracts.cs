namespace DocumentFlow.Application.Signatures;

public sealed record SignatureSummary(
    Guid Id,
    Guid DocumentId,
    Guid DocumentVersionId,
    Guid SignedByUserId,
    string SignedByName,
    string SignatureAlgorithm,
    string HashAlgorithm,
    string DocumentHash,
    string PublicKeyFingerprint,
    DateTime SignedAtUtc);

public sealed record SignatureVerification(
    Guid SignatureId,
    Guid SignedByUserId,
    string SignedByName,
    string SignatureAlgorithm,
    string HashAlgorithm,
    string PublicKeyFingerprint,
    DateTime SignedAtUtc,
    string Result,
    string Message);

public sealed record SignatureList(IReadOnlyList<SignatureSummary> Items, bool CanSign);

public interface IDocumentSignatureService
{
    Task<SignatureSummary> SignAsync(Guid documentId, Guid versionId, Guid userId, CancellationToken cancellationToken);
    Task<SignatureList?> ListAsync(Guid documentId, Guid versionId, Guid userId, bool isAdmin, CancellationToken cancellationToken);
    Task<IReadOnlyList<SignatureVerification>?> VerifyAsync(Guid documentId, Guid versionId, Guid userId, bool isAdmin, CancellationToken cancellationToken);
}

public sealed class SignatureAccessException(string message) : Exception(message);
public sealed class SignatureStateException(string message) : Exception(message);
public sealed class SignatureIntegrityException(string message) : Exception(message);
public sealed class SignatureProviderUnavailableException(string message) : Exception(message);
