using DocumentFlow.Domain;

namespace DocumentFlow.Application.Signatures;

public sealed record MobileSignatureSummary(Guid Id, Guid DocumentId, Guid DocumentVersionId, string DocumentNumber,
    string DocumentTitle, int VersionNumber, MobileSignatureStatus Status, DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc, DateTime? CompletedAtUtc, Guid? DocumentSignatureId);

public interface IMobileSignatureService
{
    Task<MobileSignatureSummary> CreateAsync(Guid documentId, Guid versionId, Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MobileSignatureSummary>> ListAsync(Guid userId, CancellationToken cancellationToken);
    Task<MobileSignatureSummary?> GetAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<MobileSignatureSummary> ApproveAsync(Guid id, Guid userId, string password, CancellationToken cancellationToken);
    Task<MobileSignatureSummary> RejectAsync(Guid id, Guid userId, CancellationToken cancellationToken);
}

public sealed class MobileSignatureAccessException(string message) : Exception(message);
public sealed class MobileSignatureStateException(string message) : Exception(message);
public sealed class MobileSignaturePasswordException : Exception;
