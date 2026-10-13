namespace DocumentFlow.Domain;

public sealed class AuditLog
{
    private AuditLog() { }

    public AuditLog(Guid id, Guid userId, AuditAction action, Guid? documentId, Guid? documentVersionId,
        Guid? approvalRequestId, string? description, DateTime createdAtUtc)
    {
        Id = id;
        UserId = userId;
        Action = action;
        DocumentId = documentId;
        DocumentVersionId = documentVersionId;
        ApprovalRequestId = approvalRequestId;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public AuditAction Action { get; private set; }
    public Guid? DocumentId { get; private set; }
    public Guid? DocumentVersionId { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public string? Description { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public User User { get; private set; } = null!;
    public Document? Document { get; private set; }
    public DocumentVersion? DocumentVersion { get; private set; }
    public ApprovalRequest? ApprovalRequest { get; private set; }
}
