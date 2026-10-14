using System.Text.Json;
using DocumentFlow.Infrastructure.Integrations;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class RabbitConsumerService(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<RabbitConsumerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await RabbitTopology.Factory(configuration).CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await RabbitTopology.DeclareAsync(channel, configuration, stoppingToken);
                await channel.BasicQosAsync(0, 10, global: false, cancellationToken: stoppingToken);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, delivery) =>
                {
                    try
                    {
                        var body = delivery.Body.ToArray();
                        if (!Guid.TryParse(delivery.BasicProperties.MessageId, out var eventId) ||
                            string.IsNullOrEmpty(delivery.BasicProperties.Type))
                            throw new InvalidDataException("Integration event header is invalid.");
                        using var scope = scopes.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<IntegrationEventIngestor>()
                            .IngestAsync(body, eventId, delivery.BasicProperties.Type, stoppingToken);
                        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
                    }
                    catch (Exception ex) when (ex is InvalidDataException or JsonException or KeyNotFoundException or FormatException)
                    {
                        logger.LogWarning("Invalid integration event sent to DLQ: {ErrorType}", ex.GetType().Name);
                        await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, cancellationToken: stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        logger.LogWarning("Integration event intake failed: {ErrorType}", ex.GetType().Name);
                        await Task.Delay(1000, stoppingToken);
                        await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
                    }
                };
                await channel.BasicConsumeAsync(RabbitTopology.QueueName(configuration), autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
                while (connection.IsOpen && !stoppingToken.IsCancellationRequested)
                    await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("RabbitMQ consumer reconnecting: {ErrorType}", ex.GetType().Name); }
            try { await Task.Delay(3000, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
