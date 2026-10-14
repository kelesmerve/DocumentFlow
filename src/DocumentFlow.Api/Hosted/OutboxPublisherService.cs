using DocumentFlow.Infrastructure.Integrations;

namespace DocumentFlow.Api.Hosted;

public sealed class OutboxPublisherService(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<OutboxPublisherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!bool.TryParse(configuration["RabbitMq:Enabled"], out var enabled) || !enabled) return;
        var pollSeconds = int.TryParse(configuration["RabbitMq:Publisher:PollSeconds"], out var configured)
            ? Math.Clamp(configured, 1, 60) : 3;
        var concurrency = int.TryParse(configuration["RabbitMq:Publisher:MaxConcurrentBatches"], out var configuredConcurrency)
            ? Math.Clamp(configuredConcurrency, 1, 4) : 1;
        await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => RunLoopAsync(pollSeconds, stoppingToken)));
    }

    private async Task RunLoopAsync(int pollSeconds, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning("Outbox publisher iteration failed: {ErrorType}", exception.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
