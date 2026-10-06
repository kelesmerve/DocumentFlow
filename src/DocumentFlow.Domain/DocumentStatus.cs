namespace DocumentFlow.Domain;

public enum DocumentStatus
{
    Draft,
    PendingApproval,
    RevisionRequested,
    Approved,
    Rejected,
    Signed,
    Archived
}
