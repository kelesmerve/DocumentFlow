namespace DocumentFlow.Application.Integrations;

public sealed record WebhookInput(string Url, IReadOnlyList<string> EventTypes, string Secret);
public sealed record WebhookSummary(Guid Id, string Url, IReadOnlyList<string> EventTypes, bool IsActive, DateTime CreatedAtUtc);
public sealed record WebhookPage(IReadOnlyList<WebhookSummary> Items, int Page, int PageSize, int TotalCount);
public sealed record DeliverySummary(Guid Id, Guid EventId, Guid WebhookEndpointId, string EventType,
    string Status, int AttemptCount, DateTime CreatedAtUtc, DateTime? DeliveredAtUtc, string? LastError);
public sealed record DeliveryPage(IReadOnlyList<DeliverySummary> Items, int Page, int PageSize, int TotalCount,
    DateTime? NextAfterCreatedAtUtc = null, Guid? NextAfterId = null);

public interface IWebhookManagementService
{
    Task<WebhookSummary> CreateAsync(Guid adminId, WebhookInput input, CancellationToken cancellationToken);
    Task<WebhookPage> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<WebhookSummary?> SetActiveAsync(Guid adminId, Guid id, bool active, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid adminId, Guid id, CancellationToken cancellationToken);
    Task<bool> RotateSecretAsync(Guid adminId, Guid id, string secret, CancellationToken cancellationToken);
    Task<DeliveryPage> DeliveriesAsync(int page, int pageSize, DateTime? afterCreatedAtUtc, Guid? afterId,
        CancellationToken cancellationToken);
}

public sealed class WebhookInputException(string message) : Exception(message);
