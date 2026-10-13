namespace DocumentFlow.Domain;

public sealed class MobileSignatureRequest
{
    private MobileSignatureRequest() { }

    public MobileSignatureRequest(Guid id, Guid documentId, Guid documentVersionId, Guid managerId,
        string fileHash, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        Id = id;
        DocumentId = documentId;
        DocumentVersionId = documentVersionId;
        ManagerId = managerId;
        FileHash = fileHash;
        CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
        ExpiresAtUtc = DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc);
        Status = MobileSignatureStatus.Pending;
    }

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid DocumentVersionId { get; private set; }
    public Guid ManagerId { get; private set; }
    public string FileHash { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public MobileSignatureStatus Status { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public Guid? DocumentSignatureId { get; private set; }
    public Document Document { get; private set; } = null!;
    public DocumentVersion DocumentVersion { get; private set; } = null!;
    public User Manager { get; private set; } = null!;
    public DocumentSignature? DocumentSignature { get; private set; }

    public void Approve(Guid signatureId, DateTime now)
    {
        if (Status != MobileSignatureStatus.Pending || ExpiresAtUtc <= now) throw new InvalidOperationException("Request is no longer pending.");
        Status = MobileSignatureStatus.Approved;
        DocumentSignatureId = signatureId;
        CompletedAtUtc = DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }

    public void Reject(DateTime now)
    {
        if (Status != MobileSignatureStatus.Pending || ExpiresAtUtc <= now) throw new InvalidOperationException("Request is no longer pending.");
        Status = MobileSignatureStatus.Rejected;
        CompletedAtUtc = DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }

    public void Expire(DateTime now)
    {
        if (Status != MobileSignatureStatus.Pending) return;
        Status = MobileSignatureStatus.Expired;
        CompletedAtUtc = DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }
}
