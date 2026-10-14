using DocumentFlow.Application.Integrations;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocumentFlow.Infrastructure.Integrations;

public sealed class WebhookManagementService(DocumentFlowDbContext db, WebhookSecretProtector secrets,
    WebhookTargetPolicy targets) : IWebhookManagementService
{
    public async Task<WebhookSummary> CreateAsync(Guid adminId, WebhookInput input, CancellationToken ct)
    {
        await ValidateAsync(input, ct);
        var now = DateTime.UtcNow;
        var endpoint = new WebhookEndpoint(Guid.NewGuid(), input.Url.Trim(),
            string.Join(',', input.EventTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
            secrets.Protect(input.Secret), now);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.WebhookEndpoints.Add(endpoint);
        AddAudit(adminId, AuditAction.WebhookCreated, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToSummary(endpoint);
    }

    public async Task<WebhookPage> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = db.WebhookEndpoints.AsNoTracking().Where(x => !x.IsDeleted);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.Url, x.EventTypes, x.IsActive, x.CreatedAtUtc }).ToListAsync(ct);
        return new WebhookPage(rows.Select(x => new WebhookSummary(x.Id, x.Url,
            x.EventTypes.Split(',', StringSplitOptions.RemoveEmptyEntries), x.IsActive, x.CreatedAtUtc)).ToArray(),
            page, pageSize, total);
    }

    public async Task<WebhookSummary?> SetActiveAsync(Guid adminId, Guid id, bool active, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var endpoint = await db.WebhookEndpoints.FromSqlInterpolated($"SELECT * FROM webhook_endpoints WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (endpoint is null || endpoint.IsDeleted) return null;
        if (endpoint.IsActive != active)
        {
            endpoint.SetActive(active);
            AddAudit(adminId, active ? AuditAction.WebhookEnabled : AuditAction.WebhookDisabled, DateTime.UtcNow);
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return ToSummary(endpoint);
    }

    public async Task<bool> DeleteAsync(Guid adminId, Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var endpoint = await db.WebhookEndpoints.FromSqlInterpolated($"SELECT * FROM webhook_endpoints WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (endpoint is null || endpoint.IsDeleted) return false;
        endpoint.Delete();
        AddAudit(adminId, AuditAction.WebhookDeleted, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> RotateSecretAsync(Guid adminId, Guid id, string secret, CancellationToken ct)
    {
        if (secret.Length is < 32 or > 256) throw new WebhookInputException("Secret must contain 32–256 characters.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var endpoint = await db.WebhookEndpoints.FromSqlInterpolated($"SELECT * FROM webhook_endpoints WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (endpoint is null || endpoint.IsDeleted) return false;
        endpoint.RotateSecret(secrets.Protect(secret));
        AddAudit(adminId, AuditAction.WebhookSecretRotated, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<DeliveryPage> DeliveriesAsync(int page, int pageSize, DateTime? afterCreatedAtUtc,
        Guid? afterId, CancellationToken ct)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = db.WebhookDeliveries.AsNoTracking();
        var total = await query.CountAsync(ct);
        var useCursor = afterCreatedAtUtc.HasValue && afterId.HasValue;
        if (useCursor)
            query = query.Where(x => x.CreatedAtUtc < afterCreatedAtUtc!.Value ||
                (x.CreatedAtUtc == afterCreatedAtUtc.Value && x.Id.CompareTo(afterId!.Value) < 0));
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip(useCursor ? 0 : (page - 1) * pageSize).Take(pageSize + (useCursor ? 1 : 0))
            .Select(x => new DeliverySummary(x.Id, x.EventId, x.WebhookEndpointId, x.EventType,
                x.Status.ToString(), x.AttemptCount, x.CreatedAtUtc, x.DeliveredAtUtc, x.LastError)).ToListAsync(ct);
        var items = rows.Take(pageSize).ToArray();
        var hasMore = useCursor ? rows.Count > pageSize : (long)page * pageSize < total;
        var last = hasMore && items.Length > 0 ? items[^1] : null;
        return new DeliveryPage(items, page, pageSize, total, last?.CreatedAtUtc, last?.Id);
    }

    private static (int Page, int PageSize) NormalizePage(int page, int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        return (Math.Min(Math.Max(page, 1), int.MaxValue / pageSize), pageSize);
    }

    private async Task ValidateAsync(WebhookInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Url) || input.Url.Length > 2048 ||
            string.IsNullOrWhiteSpace(input.Secret) || input.Secret.Length is < 32 or > 256 || input.EventTypes is null ||
            input.EventTypes.Count == 0 || input.EventTypes.Any(x => !IntegrationEventFactory.SupportedTypes.Contains(x)))
            throw new WebhookInputException("Webhook URL, secret or event subscriptions are invalid.");
        try { await targets.ValidateAsync(input.Url, ct); }
        catch (ArgumentException) { throw new WebhookInputException("Webhook target is not allowed."); }
        catch (System.Net.Sockets.SocketException) { throw new WebhookInputException("Webhook hostname could not be resolved."); }
    }

    private void AddAudit(Guid adminId, AuditAction action, DateTime now) =>
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), adminId, action, null, null, null,
            action switch { AuditAction.WebhookCreated => "Webhook eklendi.", AuditAction.WebhookDeleted => "Webhook silindi.",
                AuditAction.WebhookEnabled => "Webhook etkinleştirildi.", AuditAction.WebhookDisabled => "Webhook devre dışı bırakıldı.",
                _ => "Webhook secret yenilendi." }, now));

    private static WebhookSummary ToSummary(WebhookEndpoint endpoint) =>
        new(endpoint.Id, endpoint.Url, endpoint.EventTypes.Split(',', StringSplitOptions.RemoveEmptyEntries),
            endpoint.IsActive, endpoint.CreatedAtUtc);
}
