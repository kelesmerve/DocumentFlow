using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocumentFlow.Api.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/operations")]
public sealed class OperationsController(DocumentFlowDbContext db) : ControllerBase
{
    [HttpGet("metrics")]
    public async Task<IActionResult> Metrics(CancellationToken cancellationToken)
    {
        var outboxBacklog = await db.OutboxMessages.CountAsync(x => x.PublishedAtUtc == null, cancellationToken);
        var pendingDeliveries = await db.WebhookDeliveries.CountAsync(x =>
            x.Status == WebhookDeliveryStatus.Pending || x.Status == WebhookDeliveryStatus.DeadLetterPending,
            cancellationToken);
        var failedDeliveries = await db.WebhookDeliveries.CountAsync(x =>
            x.Status == WebhookDeliveryStatus.DeadLetter, cancellationToken);
        return Ok(new { measuredAtUtc = DateTime.UtcNow, outboxBacklog, pendingDeliveries, failedDeliveries });
    }
}
