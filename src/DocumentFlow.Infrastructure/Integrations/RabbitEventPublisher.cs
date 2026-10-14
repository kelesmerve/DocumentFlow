using System.Text;
using DocumentFlow.Domain;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace DocumentFlow.Infrastructure.Integrations;

public interface IOutboxTransport
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public static class RabbitTopology
{
    public const string Exchange = "documentflow.events.v1";
    public const string Queue = "documentflow.webhooks.v1";
    public const string DeadLetterQueue = "documentflow.webhooks.dlq.v1";
    public static string ExchangeName(IConfiguration config) => config["RabbitMq:Exchange"] ?? Exchange;
    public static string QueueName(IConfiguration config) => config["RabbitMq:Queue"] ?? Queue;
    public static string DeadLetterQueueName(IConfiguration config) => config["RabbitMq:DeadLetterQueue"] ?? DeadLetterQueue;

    public static ConnectionFactory Factory(IConfiguration config) => new()
    {
        HostName = config["RabbitMq:Host"] ?? "localhost",
        Port = int.TryParse(config["RabbitMq:Port"], out var port) ? port : 5672,
        UserName = config["RabbitMq:Username"] ?? "documentflow",
        Password = config["RabbitMq:Password"] ?? string.Empty,
        VirtualHost = config["RabbitMq:VirtualHost"] ?? "/",
        AutomaticRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
        RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
    };

    public static async Task DeclareAsync(IChannel channel, IConfiguration config, CancellationToken ct)
    {
        var exchange = ExchangeName(config); var queue = QueueName(config); var deadLetterQueue = DeadLetterQueueName(config);
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(deadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = deadLetterQueue }, cancellationToken: ct);
        foreach (var eventType in IntegrationEventFactory.SupportedTypes)
            await channel.QueueBindAsync(queue, exchange, eventType, cancellationToken: ct);
    }
}

public sealed class RabbitEventPublisher(IConfiguration configuration) : IOutboxTransport, IAsyncDisposable
{
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_channel is null || !_channel.IsOpen)
            {
                if (_channel is not null) await _channel.DisposeAsync();
                if (_connection is not null) await _connection.DisposeAsync();
                _connection = await RabbitTopology.Factory(configuration).CreateConnectionAsync(cancellationToken);
                _channel = await _connection.CreateChannelAsync(new CreateChannelOptions(
                    publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), cancellationToken);
                await RabbitTopology.DeclareAsync(_channel, configuration, cancellationToken);
            }
            var properties = new BasicProperties
            {
                Persistent = true, ContentType = "application/json", MessageId = message.EventId.ToString("D"),
                Type = message.EventType
            };
            await _channel.BasicPublishAsync(RabbitTopology.ExchangeName(configuration), message.EventType, mandatory: true,
                basicProperties: properties, body: Encoding.UTF8.GetBytes(message.Payload), cancellationToken);
        }
        catch
        {
            if (_channel is not null) { await _channel.DisposeAsync(); _channel = null; }
            if (_connection is not null) { await _connection.DisposeAsync(); _connection = null; }
            throw;
        }
        finally { _mutex.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        _mutex.Dispose();
    }
}
