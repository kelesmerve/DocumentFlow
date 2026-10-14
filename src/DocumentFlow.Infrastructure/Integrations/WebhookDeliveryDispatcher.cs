using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocumentFlow.Infrastructure.Integrations;

public sealed class WebhookDeliveryDispatcher(DocumentFlowDbContext db, IWebhookSender sender,
    IDeadLetterPublisher deadLetters)
{
    public async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var leaseId = Guid.NewGuid();
        List<WebhookDelivery> jobs;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            jobs = await db.WebhookDeliveries.FromSqlInterpolated($"""
                SELECT * FROM webhook_deliveries WHERE "Status" IN ('Pending', 'DeadLetterPending')
                AND "NextAttemptAtUtc" <= {now}
                AND ("LeaseUntilUtc" IS NULL OR "LeaseUntilUtc" <= {now})
                ORDER BY "CreatedAtUtc", "Id" FOR UPDATE SKIP LOCKED LIMIT 1
                """).ToListAsync(ct);
            foreach (var job in jobs) job.Claim(leaseId, now.AddMinutes(1));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        foreach (var job in jobs)
        {
            if (job.Status == WebhookDeliveryStatus.DeadLetterPending)
            {
                await TryDeadLetterAsync(job, ct);
                continue;
            }
            try
            {
                var endpoint = await db.WebhookEndpoints.AsNoTracking().SingleAsync(x => x.Id == job.WebhookEndpointId, ct);
                if (!endpoint.IsActive || endpoint.IsDeleted) throw new InvalidOperationException("Webhook endpoint is inactive.");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await sender.SendAsync(job, endpoint, timeout.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Min(job.AttemptCount + 1, 8))));
                job.Failed(exception.GetType().Name, DateTime.UtcNow.Add(delay), maxAttempts: 5);
                await db.SaveChangesAsync(ct);
                if (job.Status == WebhookDeliveryStatus.DeadLetterPending) await TryDeadLetterAsync(job, ct);
                continue;
            }
            job.Delivered(DateTime.UtcNow);
            await db.SaveChangesAsync(ct);
        }
        return jobs.Count;
    }

    private async Task TryDeadLetterAsync(WebhookDelivery job, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await deadLetters.PublishAsync(job, timeout.Token);
            job.DeadLettered();
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            job.RetryDeadLetter(DateTime.UtcNow.AddMinutes(1));
            await db.SaveChangesAsync(ct);
        }
    }
}
