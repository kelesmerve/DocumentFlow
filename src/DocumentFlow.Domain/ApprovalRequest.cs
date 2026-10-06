namespace DocumentFlow.Domain;

public sealed class ApprovalRequest
{
    private ApprovalRequest() { }
    public ApprovalRequest(Guid id, Guid documentId, Guid documentVersionId, Guid requestedByUserId, Guid assignedToUserId, string? requestComment, DateTime createdAtUtc)
    {
        Id = id; DocumentId = documentId; DocumentVersionId = documentVersionId; RequestedByUserId = requestedByUserId;
        AssignedToUserId = assignedToUserId; RequestComment = string.IsNullOrWhiteSpace(requestComment) ? null : requestComment.Trim();
        Status = ApprovalStatus.Pending; CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
    }
    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid DocumentVersionId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public Guid AssignedToUserId { get; private set; }
    public ApprovalStatus Status { get; private set; }
    public string? RequestComment { get; private set; }
    public string? DecisionComment { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }
    public Document Document { get; private set; } = null!;
    public DocumentVersion DocumentVersion { get; private set; } = null!;
    public User RequestedByUser { get; private set; } = null!;
    public User AssignedToUser { get; private set; } = null!;

    public void Decide(ApprovalStatus status, string? comment, DateTime atUtc)
    {
        if (Status != ApprovalStatus.Pending) throw new InvalidOperationException("Only pending approval requests can be decided.");
        if (status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected or ApprovalStatus.RevisionRequested)) throw new ArgumentOutOfRangeException(nameof(status));
        if (status is ApprovalStatus.Rejected or ApprovalStatus.RevisionRequested && string.IsNullOrWhiteSpace(comment)) throw new ArgumentException("A decision comment is required.", nameof(comment));
        Status = status; DecisionComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(); DecidedAtUtc = DateTime.SpecifyKind(atUtc, DateTimeKind.Utc);
    }
}
