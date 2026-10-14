namespace DocumentFlow.Domain;

public sealed class OutboxMessage
{
    private OutboxMessage() { }
    public OutboxMessage(Guid eventId, string eventType, Guid aggregateId, DateTime occurredAtUtc, string payload)
    {
        EventId = eventId;
        EventType = eventType;
        AggregateId = aggregateId;
        OccurredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
        SchemaVersion = 1;
        Payload = payload;
        NextAttemptAtUtc = OccurredAtUtc;
    }

    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public Guid AggregateId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public int SchemaVersion { get; private set; }
    public string Payload { get; private set; } = string.Empty;
    public DateTime? PublishedAtUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public Guid? LeaseId { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }
    public string? LastError { get; private set; }

    public void Claim(Guid leaseId, DateTime untilUtc) { LeaseId = leaseId; LeaseUntilUtc = untilUtc; }
    public void Published(DateTime atUtc) { PublishedAtUtc = atUtc; LeaseId = null; LeaseUntilUtc = null; LastError = null; }
    public void Failed(string error, DateTime nextAttemptAtUtc)
    {
        AttemptCount++;
        LastError = error.Length > 500 ? error[..500] : error;
        NextAttemptAtUtc = nextAttemptAtUtc;
        LeaseId = null;
        LeaseUntilUtc = null;
    }
}
