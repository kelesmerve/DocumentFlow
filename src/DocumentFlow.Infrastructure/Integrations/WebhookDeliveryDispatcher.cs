using System.Diagnostics;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using DocumentFlow.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DocumentFlow.Infrastructure.Integrations;

public sealed class WebhookDeliveryDispatcher(DocumentFlowDbContext db, IWebhookSender sender,
    IDeadLetterPublisher deadLetters, IConfiguration? configuration = null)
{
    public async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var leaseId = Guid.NewGuid();
        var batchSize = ReadNumber("WebhookDelivery:BatchSize", 1, 1, 10);
        var attemptTimeout = ReadNumber("WebhookDelivery:AttemptTimeoutSeconds", 10, 1, 30);
        var maxAttempts = ReadNumber("WebhookDelivery:MaxAttempts", 5, 1, 20);
        var retryBase = ReadNumber("WebhookDelivery:RetryBaseSeconds", 1, 1, 60);
        var retryMax = ReadNumber("WebhookDelivery:RetryMaxSeconds", 300, 2, 3600);
        var leaseSeconds = Math.Max(ReadNumber("WebhookDelivery:LeaseSeconds", 60, 30, 600),
            batchSize * attemptTimeout * 2 + 30);
        List<WebhookDelivery> jobs;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            jobs = await db.WebhookDeliveries.FromSqlInterpolated($"""
                SELECT * FROM webhook_deliveries WHERE "Status" IN ('Pending', 'DeadLetterPending')
                AND "NextAttemptAtUtc" <= {now}
                AND ("LeaseUntilUtc" IS NULL OR "LeaseUntilUtc" <= {now})
                ORDER BY "CreatedAtUtc", "Id" FOR UPDATE SKIP LOCKED LIMIT {batchSize}
                """).ToListAsync(ct);
            foreach (var job in jobs) job.Claim(leaseId, now.AddSeconds(leaseSeconds));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        foreach (var job in jobs)
        {
            if (job.Status == WebhookDeliveryStatus.DeadLetterPending)
            {
                await TryDeadLetterAsync(job, attemptTimeout, ct);
                continue;
            }
            var started = Stopwatch.GetTimestamp();
            try
            {
                var endpoint = await db.WebhookEndpoints.AsNoTracking().SingleAsync(x => x.Id == job.WebhookEndpointId, ct);
                if (!endpoint.IsActive || endpoint.IsDeleted) throw new InvalidOperationException("Webhook endpoint is inactive.");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(attemptTimeout));
                await sender.SendAsync(job, endpoint, timeout.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(retryMax,
                    retryBase * Math.Pow(2, Math.Min(job.AttemptCount + 1, 12))));
                job.Failed(exception.GetType().Name, DateTime.UtcNow.Add(delay), maxAttempts);
                await db.SaveChangesAsync(ct);
                DocumentFlowMetrics.WebhookFailures.Add(1,
                    new KeyValuePair<string, object?>("event_type", job.EventType));
                if (job.Status == WebhookDeliveryStatus.DeadLetterPending) await TryDeadLetterAsync(job, attemptTimeout, ct);
                continue;
            }
            finally
            {
                DocumentFlowMetrics.WebhookDeliveryDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    new KeyValuePair<string, object?>("event_type", job.EventType));
            }
            job.Delivered(DateTime.UtcNow);
            await db.SaveChangesAsync(ct);
        }
        return jobs.Count;
    }

    private async Task TryDeadLetterAsync(WebhookDelivery job, int attemptTimeout, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(attemptTimeout));
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

    private int ReadNumber(string key, int fallback, int min, int max) =>
        int.TryParse(configuration?[key], out var value) ? Math.Clamp(value, min, max) : fallback;
}
