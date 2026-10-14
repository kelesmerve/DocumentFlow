using DocumentFlow.Infrastructure.Integrations;
using DocumentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
var connection = builder.Configuration.GetConnectionString("DocumentFlow")
    ?? throw new InvalidOperationException("Worker database connection is not configured.");
builder.Services.AddDbContext<DocumentFlowDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddScoped<IntegrationEventIngestor>();
builder.Services.AddScoped<WebhookDeliveryDispatcher>();
builder.Services.AddScoped<WebhookSecretProtector>();
builder.Services.AddScoped<WebhookTargetPolicy>();
builder.Services.AddScoped<IWebhookSender, WebhookSender>();
builder.Services.AddScoped<IDeadLetterPublisher, DeadLetterPublisher>();
builder.Services.AddHostedService<RabbitConsumerService>();
builder.Services.AddHostedService<WebhookDeliveryService>();
await builder.Build().RunAsync();
