namespace DocumentFlow.Application.Timestamps;

public sealed record TimestampIssue(byte[] Token, string Authority, string SerialNumber, string Algorithm,
    string Imprint, DateTime GenTimeUtc, string ProviderType);
public sealed record TimestampVerification(string Result, string Message, string? Authority, string? SerialNumber,
    string? Algorithm, string? Imprint, DateTime? GenTimeUtc);
public sealed record TimestampSummary(Guid Id, Guid DocumentSignatureId, string TimestampAuthority,
    string SerialNumber, string MessageImprintAlgorithm, string MessageImprint, DateTime GenTimeUtc,
    DateTime CreatedAtUtc, string ProviderType);
public sealed record TimestampCreateResponse(TimestampSummary Timestamp, TimestampVerification Verification);

public interface ITimestampProvider
{
    string ProviderType { get; }
    Task<TimestampIssue> IssueAsync(byte[] signatureBytes, byte[] nonce, CancellationToken cancellationToken);
    TimestampVerification Verify(byte[] token, byte[] expectedSignatureBytes, byte[] expectedNonce);
}

public interface ISignatureTimestampService
{
    Task<TimestampCreateResponse> CreateAsync(Guid signatureId, Guid userId, bool isAdmin, CancellationToken cancellationToken);
    Task<IReadOnlyList<TimestampSummary>?> ListAsync(Guid signatureId, Guid userId, bool isAdmin, CancellationToken cancellationToken);
    Task<TimestampVerification?> VerifyAsync(Guid signatureId, Guid timestampId, Guid userId, bool isAdmin, CancellationToken cancellationToken);
}

public sealed class TimestampAccessException(string message) : Exception(message);
public sealed class TimestampStateException(string message) : Exception(message);
public sealed class TimestampProviderUnavailableException(string message) : Exception(message);
