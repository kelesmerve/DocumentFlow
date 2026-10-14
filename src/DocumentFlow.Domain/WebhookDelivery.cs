namespace DocumentFlow.Domain;

public enum WebhookDeliveryStatus { Pending, Delivered, DeadLetterPending, DeadLetter }

public sealed class WebhookDelivery
{
    private WebhookDelivery() { }
    public WebhookDelivery(Guid id, Guid eventId, Guid endpointId, string eventType, string payload, DateTime createdAtUtc)
    {
        Id = id; EventId = eventId; WebhookEndpointId = endpointId; EventType = eventType; Payload = payload;
        CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc); NextAttemptAtUtc = CreatedAtUtc;
        Status = WebhookDeliveryStatus.Pending;
    }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid WebhookEndpointId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public WebhookDeliveryStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public DateTime? DeliveredAtUtc { get; private set; }
    public Guid? LeaseId { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }
    public string? LastError { get; private set; }
    public WebhookEndpoint WebhookEndpoint { get; private set; } = null!;
    public void Claim(Guid leaseId, DateTime untilUtc) { LeaseId = leaseId; LeaseUntilUtc = untilUtc; }
    public void Delivered(DateTime atUtc)
    { Status = WebhookDeliveryStatus.Delivered; DeliveredAtUtc = atUtc; AttemptCount++; LeaseId = null; LeaseUntilUtc = null; LastError = null; }
    public void Failed(string error, DateTime nextAttemptAtUtc, int maxAttempts)
    {
        AttemptCount++; LastError = error.Length > 500 ? error[..500] : error;
        NextAttemptAtUtc = nextAttemptAtUtc; LeaseId = null; LeaseUntilUtc = null;
        if (AttemptCount >= maxAttempts) Status = WebhookDeliveryStatus.DeadLetterPending;
    }
    public void DeadLettered() { Status = WebhookDeliveryStatus.DeadLetter; LeaseId = null; LeaseUntilUtc = null; }
    public void RetryDeadLetter(DateTime nextAttemptAtUtc)
    { NextAttemptAtUtc = DateTime.SpecifyKind(nextAttemptAtUtc, DateTimeKind.Utc); LeaseId = null; LeaseUntilUtc = null; }
}
