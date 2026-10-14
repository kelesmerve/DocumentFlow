using System.Text;
using System.Text.Json;
using DocumentFlow.Domain;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace DocumentFlow.Infrastructure.Integrations;

public interface IDeadLetterPublisher
{
    Task PublishAsync(WebhookDelivery delivery, CancellationToken ct);
}

public sealed class DeadLetterPublisher(IConfiguration configuration) : IDeadLetterPublisher
{
    public async Task PublishAsync(WebhookDelivery delivery, CancellationToken ct)
    {
        await using var connection = await RabbitTopology.Factory(configuration).CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(
            publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        await RabbitTopology.DeclareAsync(channel, configuration, ct);
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            deliveryId = delivery.Id, delivery.EventId, delivery.EventType,
            delivery.AttemptCount, delivery.LastError
        });
        await channel.BasicPublishAsync(string.Empty, RabbitTopology.DeadLetterQueueName(configuration), mandatory: true,
            basicProperties: new BasicProperties { Persistent = true, ContentType = "application/json",
                MessageId = delivery.Id.ToString("D") }, body: body, cancellationToken: ct);
    }
}
