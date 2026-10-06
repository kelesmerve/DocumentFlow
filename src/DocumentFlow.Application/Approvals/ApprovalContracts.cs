using DocumentFlow.Domain;

namespace DocumentFlow.Application.Approvals;

public sealed record ManagerSummary(Guid Id, string FirstName, string LastName, string Email);
public sealed record SubmitApprovalInput(Guid ManagerId, string? Comment);
public sealed record ApprovalInput(string? Comment);
public sealed record ApprovalPage(IReadOnlyList<ApprovalSummary> Items, int Page, int PageSize, int TotalCount);
public sealed record ApprovalSummary(Guid Id, Guid DocumentId, string DocumentNumber, string DocumentTitle, string Category, int VersionNumber, string RequestedBy, string RequestedByEmail, string? RequestComment, ApprovalStatus Status, DateTime CreatedAtUtc);
public sealed record ApprovalDetails(ApprovalSummary Approval, string Description, string OriginalFileName, string ContentType, long FileSize, string FileHash, string AssignedTo, string? DecisionComment, DateTime? DecidedAtUtc);
public sealed record WorkflowEvent(string Kind, string Title, string Actor, string? Detail, int? VersionNumber, DateTime AtUtc);

public interface IApprovalService
{
    Task<IReadOnlyList<ManagerSummary>> ListManagersAsync(CancellationToken cancellationToken);
    Task<ApprovalPage> ListAsync(Guid userId, bool isAdmin, int page, int pageSize, CancellationToken cancellationToken);
    Task<ApprovalDetails?> GetAsync(Guid id, Guid userId, bool isAdmin, CancellationToken cancellationToken);
    Task<ApprovalDetails> SubmitAsync(Guid documentId, Guid ownerId, SubmitApprovalInput input, CancellationToken cancellationToken);
    Task<ApprovalDetails?> DecideAsync(Guid id, Guid managerId, ApprovalStatus decision, string? comment, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkflowEvent>?> GetTimelineAsync(Guid documentId, Guid userId, bool isAdmin, CancellationToken cancellationToken);
    Task<bool> CanAccessDocumentAsync(Guid documentId, Guid userId, bool isAdmin, CancellationToken cancellationToken);
}

public sealed class ApprovalInputException(string message) : Exception(message);
