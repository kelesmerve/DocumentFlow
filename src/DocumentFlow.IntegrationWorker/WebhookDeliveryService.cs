using DocumentFlow.Infrastructure.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class WebhookDeliveryService(IServiceScopeFactory scopes,
    ILogger<WebhookDeliveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
            try { await Task.Delay(3000, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
