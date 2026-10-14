namespace DocumentFlow.Domain;

public sealed class ConsumedIntegrationEvent
{
    private ConsumedIntegrationEvent() { }
    public ConsumedIntegrationEvent(Guid eventId, DateTime consumedAtUtc)
    { EventId = eventId; ConsumedAtUtc = DateTime.SpecifyKind(consumedAtUtc, DateTimeKind.Utc); }
    public Guid EventId { get; private set; }
    public DateTime ConsumedAtUtc { get; private set; }
}
