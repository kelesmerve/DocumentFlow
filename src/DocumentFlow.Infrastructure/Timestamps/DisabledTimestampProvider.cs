using DocumentFlow.Application.Timestamps;

namespace DocumentFlow.Infrastructure.Timestamps;

public sealed class DisabledTimestampProvider : ITimestampProvider
{
    public string ProviderType => "Disabled";
    public Task<TimestampIssue> IssueAsync(byte[] signatureBytes, byte[] nonce, CancellationToken cancellationToken) =>
        throw new TimestampProviderUnavailableException("No timestamp provider is configured.");
    public TimestampVerification Verify(byte[] token, byte[] expectedSignatureBytes, byte[] expectedNonce) =>
        new("Unverifiable", "No trusted timestamp provider is configured.", null, null, null, null, null);
}
