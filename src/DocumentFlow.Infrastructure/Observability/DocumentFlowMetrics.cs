using System.Diagnostics.Metrics;

namespace DocumentFlow.Infrastructure.Observability;

public static class DocumentFlowMetrics
{
    public static readonly Meter Meter = new("DocumentFlow", "1.0.0");
    public static readonly Counter<long> ApiRequests = Meter.CreateCounter<long>("documentflow.api.requests");
    public static readonly Histogram<double> ApiRequestDuration = Meter.CreateHistogram<double>("documentflow.api.request.duration", "ms");
    public static readonly Counter<long> OutboxPublished = Meter.CreateCounter<long>("documentflow.outbox.published");
    public static readonly Counter<long> OutboxFailures = Meter.CreateCounter<long>("documentflow.outbox.failures");
    public static readonly Histogram<double> OutboxPublishDuration = Meter.CreateHistogram<double>("documentflow.outbox.publish.duration", "ms");
    public static readonly Counter<long> WebhookFailures = Meter.CreateCounter<long>("documentflow.webhook.failures");
    public static readonly Histogram<double> WebhookDeliveryDuration = Meter.CreateHistogram<double>("documentflow.webhook.delivery.duration", "ms");
}
