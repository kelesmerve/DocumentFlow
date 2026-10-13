namespace DocumentFlow.Domain;

public sealed class DocumentTimestamp
{
    private DocumentTimestamp() { }

    public DocumentTimestamp(Guid id, Guid documentSignatureId, string timestampToken, string timestampAuthority,
        string serialNumber, string messageImprintAlgorithm, string messageImprint, DateTime genTimeUtc,
        DateTime createdAtUtc, string providerType, string nonce)
    {
        Id = id;
        DocumentSignatureId = documentSignatureId;
        TimestampToken = timestampToken;
        TimestampAuthority = timestampAuthority;
        SerialNumber = serialNumber;
        MessageImprintAlgorithm = messageImprintAlgorithm;
        MessageImprint = messageImprint;
        GenTimeUtc = DateTime.SpecifyKind(genTimeUtc, DateTimeKind.Utc);
        CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
        ProviderType = providerType;
        Nonce = nonce;
    }

    public Guid Id { get; private set; }
    public Guid DocumentSignatureId { get; private set; }
    public string TimestampToken { get; private set; } = string.Empty;
    public string TimestampAuthority { get; private set; } = string.Empty;
    public string SerialNumber { get; private set; } = string.Empty;
    public string MessageImprintAlgorithm { get; private set; } = string.Empty;
    public string MessageImprint { get; private set; } = string.Empty;
    public DateTime GenTimeUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string ProviderType { get; private set; } = string.Empty;
    public string Nonce { get; private set; } = string.Empty;
    public DocumentSignature DocumentSignature { get; private set; } = null!;
}
