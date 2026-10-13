namespace DocumentFlow.Domain;

public enum AuditAction
{
    DocumentCreated,
    DocumentVersionUploaded,
    ApprovalSubmitted,
    ApprovalApproved,
    ApprovalRejected,
    RevisionRequested,
    DocumentDownloaded,
    DocumentSigned,
    TimestampCreated
}
