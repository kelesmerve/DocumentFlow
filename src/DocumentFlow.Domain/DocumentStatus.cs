namespace DocumentFlow.Domain;

public enum DocumentStatus
{
    Draft,
    PendingApproval,
    Approved,
    Rejected,
    Signed,
    Archived
}
