using System.Diagnostics;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using DocumentFlow.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DocumentFlow.Infrastructure.Integrations;

public sealed class OutboxDispatcher(DocumentFlowDbContext db, IOutboxTransport transport, IConfiguration? configuration = null)
{
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var leaseId = Guid.NewGuid();
        var batchSize = ReadNumber("RabbitMq:Publisher:BatchSize", 1, 1, 10);
        var publishTimeout = ReadNumber("RabbitMq:Publisher:PublishTimeoutSeconds", 15, 1, 30);
        var concurrency = ReadNumber("RabbitMq:Publisher:MaxConcurrentBatches", 1, 1, 4);
        var retryBase = ReadNumber("RabbitMq:Publisher:RetryBaseSeconds", 1, 1, 60);
        var retryMax = ReadNumber("RabbitMq:Publisher:RetryMaxSeconds", 300, 2, 3600);
        var leaseSeconds = Math.Max(ReadNumber("RabbitMq:Publisher:LeaseSeconds", 60, 30, 600),
            batchSize * publishTimeout * concurrency + 30);
        List<OutboxMessage> messages;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            messages = await db.OutboxMessages.FromSqlInterpolated($"""
                SELECT * FROM outbox_messages WHERE "PublishedAtUtc" IS NULL
                AND "NextAttemptAtUtc" <= {now}
                AND ("LeaseUntilUtc" IS NULL OR "LeaseUntilUtc" <= {now})
                ORDER BY "OccurredAtUtc", "EventId" FOR UPDATE SKIP LOCKED LIMIT {batchSize}
                """).ToListAsync(cancellationToken);
            foreach (var message in messages) message.Claim(leaseId, now.AddSeconds(leaseSeconds));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        foreach (var message in messages)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(publishTimeout));
                await transport.PublishAsync(message, timeout.Token);
                var updated = await db.OutboxMessages.Where(x => x.EventId == message.EventId && x.LeaseId == leaseId && x.PublishedAtUtc == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.PublishedAtUtc, DateTime.UtcNow)
                        .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseUntilUtc, (DateTime?)null)
                        .SetProperty(x => x.LastError, (string?)null), cancellationToken);
                if (updated == 1) DocumentFlowMetrics.OutboxPublished.Add(1, new KeyValuePair<string, object?>("event_type", message.EventType));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                var attempt = message.AttemptCount + 1;
                var seconds = Math.Min(retryMax, retryBase * Math.Pow(2, Math.Min(attempt, 12)));
                var updated = await db.OutboxMessages.Where(x => x.EventId == message.EventId && x.LeaseId == leaseId && x.PublishedAtUtc == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.AttemptCount, attempt)
                        .SetProperty(x => x.NextAttemptAtUtc, DateTime.UtcNow.AddSeconds(seconds))
                        .SetProperty(x => x.LastError, exception.GetType().Name)
                        .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseUntilUtc, (DateTime?)null), cancellationToken);
                if (updated == 1) DocumentFlowMetrics.OutboxFailures.Add(1, new KeyValuePair<string, object?>("event_type", message.EventType));
            }
            finally
            {
                DocumentFlowMetrics.OutboxPublishDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    new KeyValuePair<string, object?>("event_type", message.EventType));
            }
        }
        return messages.Count;
    }

    private int ReadNumber(string key, int fallback, int min, int max) =>
        int.TryParse(configuration?[key], out var value) ? Math.Clamp(value, min, max) : fallback;
}
