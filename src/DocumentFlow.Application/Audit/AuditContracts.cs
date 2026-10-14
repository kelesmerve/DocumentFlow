using DocumentFlow.Domain;

namespace DocumentFlow.Application.Audit;

public sealed record AuditEntry(
    Guid Id,
    AuditAction Action,
    string ActionDisplayName,
    Guid UserId,
    string UserName,
    string UserEmail,
    Guid? DocumentId,
    string? DocumentNumber,
    string? DocumentTitle,
    Guid? DocumentVersionId,
    int? VersionNumber,
    Guid? ApprovalRequestId,
    string? Description,
    DateTime CreatedAtUtc);

public sealed record AuditPage(IReadOnlyList<AuditEntry> Items, int Page, int PageSize, int TotalCount,
    DateTime? NextAfterCreatedAtUtc = null, Guid? NextAfterId = null);

public interface IAuditQueryService
{
    Task<AuditPage> ListAsync(AuditAction? action, Guid? userId, string? documentNumber, int page, int pageSize,
        DateTime? afterCreatedAtUtc, Guid? afterId, CancellationToken cancellationToken);
}
