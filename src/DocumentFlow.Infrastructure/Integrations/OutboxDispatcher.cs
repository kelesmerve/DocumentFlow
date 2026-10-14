using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocumentFlow.Infrastructure.Integrations;

public sealed class OutboxDispatcher(DocumentFlowDbContext db, IOutboxTransport transport)
{
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var leaseId = Guid.NewGuid();
        List<OutboxMessage> messages;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            messages = await db.OutboxMessages.FromSqlInterpolated($"""
                SELECT * FROM outbox_messages WHERE "PublishedAtUtc" IS NULL
                AND "NextAttemptAtUtc" <= {now}
                AND ("LeaseUntilUtc" IS NULL OR "LeaseUntilUtc" <= {now})
                ORDER BY "OccurredAtUtc", "EventId" FOR UPDATE SKIP LOCKED LIMIT 1
                """).ToListAsync(cancellationToken);
            foreach (var message in messages) message.Claim(leaseId, now.AddMinutes(1));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        foreach (var message in messages)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                await transport.PublishAsync(message, timeout.Token);
                await db.OutboxMessages.Where(x => x.EventId == message.EventId && x.LeaseId == leaseId && x.PublishedAtUtc == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.PublishedAtUtc, DateTime.UtcNow)
                        .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseUntilUtc, (DateTime?)null)
                        .SetProperty(x => x.LastError, (string?)null), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                var attempt = message.AttemptCount + 1;
                var seconds = Math.Min(300, Math.Pow(2, Math.Min(attempt, 8)));
                await db.OutboxMessages.Where(x => x.EventId == message.EventId && x.LeaseId == leaseId && x.PublishedAtUtc == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.AttemptCount, attempt)
                        .SetProperty(x => x.NextAttemptAtUtc, DateTime.UtcNow.AddSeconds(seconds))
                        .SetProperty(x => x.LastError, exception.GetType().Name)
                        .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseUntilUtc, (DateTime?)null), cancellationToken);
            }
        }
        return messages.Count;
    }
}
