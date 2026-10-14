using DocumentFlow.Infrastructure.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class WebhookDeliveryService(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<WebhookDeliveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollSeconds = int.TryParse(configuration["WebhookDelivery:PollSeconds"], out var configured)
            ? Math.Clamp(configured, 1, 60) : 3;
        var concurrency = int.TryParse(configuration["WebhookDelivery:MaxConcurrentBatches"], out var configuredConcurrency)
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
                await scope.ServiceProvider.GetRequiredService<WebhookDeliveryDispatcher>().DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Webhook delivery iteration failed: {ErrorType}", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
