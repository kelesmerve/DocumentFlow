using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Integrations;
using DocumentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace DocumentFlow.IntegrationTests;

public sealed class IntegrationPipelineTests
{
    [Fact]
    public async Task Outbox_retries_after_transport_failure_and_marks_published_only_after_confirm()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();
        var message = IntegrationEventFactory.Create(IntegrationEventFactory.DocumentApproved, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            db.OutboxMessages.Add(message);
            await db.SaveChangesAsync();
        }
        var transport = new FakeTransport { Fail = true };
        await Dispatch(factory, transport);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            var stored = await db.OutboxMessages.AsNoTracking().SingleAsync(x => x.EventId == message.EventId);
            Assert.Null(stored.PublishedAtUtc);
            Assert.Equal(1, stored.AttemptCount);
            Assert.NotNull(stored.LastError);
            await db.OutboxMessages.Where(x => x.EventId == message.EventId).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAtUtc, DateTime.UtcNow.AddSeconds(-1)));
        }
        transport.Fail = false;
        await Dispatch(factory, transport);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            var stored = await db.OutboxMessages.AsNoTracking().SingleAsync(x => x.EventId == message.EventId);
            Assert.NotNull(stored.PublishedAtUtc);
            Assert.Equal(2, transport.Calls);
        }
        await Dispatch(factory, transport);
        Assert.Equal(2, transport.Calls);
    }

    [Fact]
    public async Task Concurrent_outbox_dispatchers_claim_each_event_once()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();
        var message = IntegrationEventFactory.Create(IntegrationEventFactory.DocumentApproved, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            db.OutboxMessages.Add(message);
            await db.SaveChangesAsync();
        }
        var transport = new DelayedTransport();
        await Task.WhenAll(Dispatch(factory, transport), Dispatch(factory, transport));
        Assert.Equal(1, transport.Calls);
        await using var checkScope = factory.Services.CreateAsyncScope();
        Assert.NotNull(await checkScope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>().OutboxMessages
            .Where(x => x.EventId == message.EventId).Select(x => x.PublishedAtUtc).SingleAsync());
    }

    [Fact]
    public async Task Configured_outbox_batch_publishes_bounded_messages_without_duplicate_claims()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
        for (var i = 0; i < 2; i++)
            db.OutboxMessages.Add(IntegrationEventFactory.Create(IntegrationEventFactory.DocumentApproved,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddMilliseconds(i)));
        await db.SaveChangesAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Publisher:BatchSize"] = "2",
            ["RabbitMq:Publisher:LeaseSeconds"] = "30",
            ["RabbitMq:Publisher:PublishTimeoutSeconds"] = "15"
        }).Build();
        var transport = new FakeTransport();
        Assert.Equal(2, await new OutboxDispatcher(db, transport, configuration).DispatchBatchAsync(CancellationToken.None));
        Assert.Equal(2, transport.Calls);
        Assert.Equal(2, await db.OutboxMessages.AsNoTracking().CountAsync(x => x.PublishedAtUtc != null));
    }

    [Fact]
    public async Task Consumer_is_idempotent_and_failed_webhook_retries_then_enters_dlq()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();
        var eventMessage = IntegrationEventFactory.Create(IntegrationEventFactory.DocumentSigned, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        Guid endpointId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            var protector = new WebhookSecretProtector(scope.ServiceProvider.GetRequiredService<IConfiguration>());
            var endpoint = new WebhookEndpoint(Guid.NewGuid(), "https://1.1.1.1/webhook", IntegrationEventFactory.DocumentSigned,
                protector.Protect(new string('s', 32)), DateTime.UtcNow);
            endpointId = endpoint.Id;
            db.WebhookEndpoints.Add(endpoint);
            await db.SaveChangesAsync();
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var ingestor = new IntegrationEventIngestor(scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>());
            Assert.Equal(1, await ingestor.IngestAsync(Encoding.UTF8.GetBytes(eventMessage.Payload), eventMessage.EventId, eventMessage.EventType, CancellationToken.None));
            Assert.Equal(0, await ingestor.IngestAsync(Encoding.UTF8.GetBytes(eventMessage.Payload), eventMessage.EventId, eventMessage.EventType, CancellationToken.None));
        }
        var sender = new FakeSender { Fail = true };
        var dlq = new FakeDeadLetters();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            await db.WebhookDeliveries.Where(x => x.EventId == eventMessage.EventId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAtUtc, DateTime.UtcNow.AddSeconds(-1)));
            await new WebhookDeliveryDispatcher(db, sender, dlq).DispatchBatchAsync(CancellationToken.None);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            var delivery = await db.WebhookDeliveries.AsNoTracking().SingleAsync(x => x.EventId == eventMessage.EventId);
            Assert.Equal(endpointId, delivery.WebhookEndpointId);
            Assert.Equal(WebhookDeliveryStatus.DeadLetter, delivery.Status);
            Assert.Equal(5, delivery.AttemptCount);
            Assert.Equal(1, dlq.Calls);
            Assert.Equal(1, await db.ConsumedIntegrationEvents.CountAsync(x => x.EventId == eventMessage.EventId));
            Assert.Equal(1, await db.WebhookDeliveries.CountAsync(x => x.EventId == eventMessage.EventId));
        }
    }

    [Fact]
    public async Task Webhook_success_is_persisted_and_duplicate_event_does_not_resend()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();
        var message = IntegrationEventFactory.Create(IntegrationEventFactory.DocumentApproved, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            var secret = new WebhookSecretProtector(scope.ServiceProvider.GetRequiredService<IConfiguration>()).Protect(new string('x', 32));
            db.WebhookEndpoints.Add(new WebhookEndpoint(Guid.NewGuid(), "https://1.1.1.1/hook", message.EventType, secret, DateTime.UtcNow));
            await db.SaveChangesAsync();
            Assert.Equal(1, await new IntegrationEventIngestor(db).IngestAsync(Encoding.UTF8.GetBytes(message.Payload), message.EventId, message.EventType, CancellationToken.None));
        }
        var sender = new CountingSender();
        await using (var scope = factory.Services.CreateAsyncScope())
            await new WebhookDeliveryDispatcher(scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>(), sender, new FakeDeadLetters())
                .DispatchBatchAsync(CancellationToken.None);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
            Assert.Equal(0, await new IntegrationEventIngestor(db).IngestAsync(Encoding.UTF8.GetBytes(message.Payload), message.EventId, message.EventType, CancellationToken.None));
            var delivery = await db.WebhookDeliveries.AsNoTracking().SingleAsync(x => x.EventId == message.EventId);
            Assert.Equal(WebhookDeliveryStatus.Delivered, delivery.Status);
            Assert.NotNull(delivery.DeliveredAtUtc);
        }
        Assert.Equal(1, sender.Calls);
    }

    [Fact]
    public async Task Admin_webhook_management_hides_secret_audits_changes_and_blocks_private_targets()
    {
        await using var factory = new DocumentApiFactory();
        using var admin = await Login(factory, "admin@documentflow.local", "DevAdmin!2026");
        using var employee = await Login(factory, "employee@documentflow.local", "DevEmployee!2026");
        using var manager = await Login(factory, "manager@documentflow.local", "DevManager!2026");
        using var deniedEmployee = await employee.GetAsync("/api/integrations/webhooks");
        using var deniedManager = await manager.GetAsync("/api/integrations/webhooks");
        Assert.Equal(HttpStatusCode.Forbidden, deniedEmployee.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedManager.StatusCode);
        using var privateTarget = await admin.PostAsJsonAsync("/api/integrations/webhooks", new
        { url = "https://127.0.0.1/hook", secret = new string('s', 32), eventTypes = new[] { IntegrationEventFactory.DocumentApproved } });
        Assert.Equal(HttpStatusCode.BadRequest, privateTarget.StatusCode);
        const string secret = "this-secret-is-only-for-integration-testing-2026";
        using var created = await admin.PostAsJsonAsync("/api/integrations/webhooks", new
        { url = "https://1.1.1.1/hook", secret, eventTypes = new[] { IntegrationEventFactory.DocumentApproved } });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var body = await created.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, body);
        Assert.DoesNotContain("encryptedSecret", body, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        var id = json.RootElement.GetProperty("id").GetGuid();
        using var disabled = await admin.PatchAsJsonAsync($"/api/integrations/webhooks/{id}/active", new { isActive = false });
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        using var deleted = await admin.DeleteAsync($"/api/integrations/webhooks/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>();
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.Action == AuditAction.WebhookCreated));
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.Action == AuditAction.WebhookDisabled));
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.Action == AuditAction.WebhookDeleted));
        Assert.DoesNotContain(secret, await db.WebhookEndpoints.Where(x => x.Id == id).Select(x => x.EncryptedSecret).SingleAsync());
    }

    [Fact]
    public void Webhook_hmac_matches_sha256_of_exact_payload()
    {
        const string secret = "a-32-character-secret-for-test-2026";
        const string payload = "{\"eventId\":\"00112233-4455-6677-8899-aabbccddeeff\"}";
        var expected = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        Assert.Equal(expected, WebhookSender.Signature(secret, payload));
        Assert.NotEqual(expected, WebhookSender.Signature(secret, payload + " "));
    }

    [Fact]
    public async Task Rabbit_publisher_waits_for_confirm_on_isolated_test_topology()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();
        var baseConfig = factory.Services.GetRequiredService<IConfiguration>();
        var suffix = Guid.NewGuid().ToString("N");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = baseConfig["RabbitMq:Host"], ["RabbitMq:Port"] = baseConfig["RabbitMq:Port"],
            ["RabbitMq:Username"] = baseConfig["RabbitMq:Username"], ["RabbitMq:Password"] = baseConfig["RabbitMq:Password"],
            ["RabbitMq:Exchange"] = $"documentflow.tests.{suffix}", ["RabbitMq:Queue"] = $"documentflow.tests.{suffix}.queue",
            ["RabbitMq:DeadLetterQueue"] = $"documentflow.tests.{suffix}.dlq"
        }).Build();
        var message = IntegrationEventFactory.Create(IntegrationEventFactory.DocumentApproved, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        await using var publisher = new RabbitEventPublisher(config);
        await using var connection = await RabbitTopology.Factory(config).CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        try
        {
            await publisher.PublishAsync(message, CancellationToken.None);
            var received = await channel.BasicGetAsync(RabbitTopology.QueueName(config), autoAck: false);
            Assert.NotNull(received);
            Assert.Equal(message.EventId.ToString("D"), received.BasicProperties.MessageId);
            Assert.Equal(message.Payload, Encoding.UTF8.GetString(received.Body.Span));
            await channel.BasicAckAsync(received.DeliveryTag, false);
            var failedDelivery = new WebhookDelivery(Guid.NewGuid(), message.EventId, Guid.NewGuid(), message.EventType, message.Payload, DateTime.UtcNow);
            await new DeadLetterPublisher(config).PublishAsync(failedDelivery, CancellationToken.None);
            var deadLettered = await channel.BasicGetAsync(RabbitTopology.DeadLetterQueueName(config), autoAck: false);
            Assert.NotNull(deadLettered);
            Assert.Equal(failedDelivery.Id.ToString("D"), deadLettered.BasicProperties.MessageId);
            await channel.BasicAckAsync(deadLettered.DeliveryTag, false);
        }
        finally
        {
            await channel.QueueDeleteAsync(RabbitTopology.QueueName(config));
            await channel.QueueDeleteAsync(RabbitTopology.DeadLetterQueueName(config));
            await channel.ExchangeDeleteAsync(RabbitTopology.ExchangeName(config));
        }
    }

    private static async Task Dispatch(DocumentApiFactory factory, IOutboxTransport transport)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await new OutboxDispatcher(scope.ServiceProvider.GetRequiredService<DocumentFlowDbContext>(), transport)
            .DispatchBatchAsync(CancellationToken.None);
    }

    private static async Task<HttpClient> Login(DocumentApiFactory factory, string email, string password)
    {
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    private sealed class FakeTransport : IOutboxTransport
    {
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
        { Calls++; return Fail ? Task.FromException(new IOException("Broker unavailable")) : Task.CompletedTask; }
    }

    private sealed class FakeSender : IWebhookSender
    {
        public bool Fail { get; set; }
        public Task SendAsync(WebhookDelivery delivery, WebhookEndpoint endpoint, CancellationToken ct) =>
            Fail ? Task.FromException(new HttpRequestException("Webhook unavailable")) : Task.CompletedTask;
    }

    private sealed class CountingSender : IWebhookSender
    {
        public int Calls { get; private set; }
        public Task SendAsync(WebhookDelivery delivery, WebhookEndpoint endpoint, CancellationToken ct)
        { Calls++; return Task.CompletedTask; }
    }

    private sealed class DelayedTransport : IOutboxTransport
    {
        private int _calls;
        public int Calls => _calls;
        public async Task PublishAsync(OutboxMessage message, CancellationToken ct)
        { Interlocked.Increment(ref _calls); await Task.Delay(150, ct); }
    }

    private sealed class FakeDeadLetters : IDeadLetterPublisher
    {
        public int Calls { get; private set; }
        public Task PublishAsync(WebhookDelivery delivery, CancellationToken ct) { Calls++; return Task.CompletedTask; }
    }
}
