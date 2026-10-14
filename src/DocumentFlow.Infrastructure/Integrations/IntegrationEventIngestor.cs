using System.Text.Json;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DocumentFlow.Infrastructure.Integrations;

public sealed class IntegrationEventIngestor(DocumentFlowDbContext db)
{
    public async Task<int> IngestAsync(ReadOnlyMemory<byte> body, Guid eventId, string eventType, CancellationToken ct)
    {
        if (!IntegrationEventFactory.SupportedTypes.Contains(eventType)) throw new InvalidDataException("Unknown integration event type.");
        using var json = JsonDocument.Parse(body);
        if (json.RootElement.GetProperty("eventId").GetGuid() != eventId ||
            json.RootElement.GetProperty("eventType").GetString() != eventType ||
            json.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Integration event envelope is invalid.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await db.ConsumedIntegrationEvents.AnyAsync(x => x.EventId == eventId, ct)) return 0;
        var payload = System.Text.Encoding.UTF8.GetString(body.Span);
        var endpoints = await db.WebhookEndpoints.AsNoTracking().Where(x => x.IsActive && !x.IsDeleted).ToListAsync(ct);
        var matching = endpoints.Where(x => x.EventTypes.Split(',').Contains(eventType)).ToArray();
        var now = DateTime.UtcNow;
        db.ConsumedIntegrationEvents.Add(new ConsumedIntegrationEvent(eventId, now));
        foreach (var endpoint in matching)
            db.WebhookDeliveries.Add(new WebhookDelivery(Guid.NewGuid(), eventId, endpoint.Id, eventType, payload, now));
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return 0; }
        return matching.Length;
    }
}
