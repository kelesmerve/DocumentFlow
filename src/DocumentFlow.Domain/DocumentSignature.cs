namespace DocumentFlow.Domain;

public sealed class DocumentSignature
{
    private DocumentSignature() { }

    public DocumentSignature(Guid id, Guid documentId, Guid documentVersionId, Guid signedByUserId,
        string signatureAlgorithm, string hashAlgorithm, string documentHash, string signatureValue,
        string publicKeyFingerprint, DateTime signedAtUtc)
    {
        Id = id;
        DocumentId = documentId;
        DocumentVersionId = documentVersionId;
        SignedByUserId = signedByUserId;
        SignatureAlgorithm = signatureAlgorithm;
        HashAlgorithm = hashAlgorithm;
        DocumentHash = documentHash;
        SignatureValue = signatureValue;
        PublicKeyFingerprint = publicKeyFingerprint;
        SignedAtUtc = DateTime.SpecifyKind(signedAtUtc, DateTimeKind.Utc);
    }

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid DocumentVersionId { get; private set; }
    public Guid SignedByUserId { get; private set; }
    public string SignatureAlgorithm { get; private set; } = string.Empty;
    public string HashAlgorithm { get; private set; } = string.Empty;
    public string DocumentHash { get; private set; } = string.Empty;
    public string SignatureValue { get; private set; } = string.Empty;
    public string PublicKeyFingerprint { get; private set; } = string.Empty;
    public DateTime SignedAtUtc { get; private set; }

    public Document Document { get; private set; } = null!;
    public DocumentVersion DocumentVersion { get; private set; } = null!;
    public User SignedByUser { get; private set; } = null!;
}
