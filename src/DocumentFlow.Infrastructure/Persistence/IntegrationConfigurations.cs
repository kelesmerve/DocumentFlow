using DocumentFlow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentFlow.Infrastructure.Persistence;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages"); b.HasKey(x => x.EventId);
        b.Property(x => x.EventType).HasMaxLength(80).IsRequired();
        b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.LastError).HasMaxLength(500);
        b.HasIndex(x => new { x.EventType, x.AggregateId }).IsUnique();
        b.HasIndex(x => new { x.NextAttemptAtUtc, x.LeaseUntilUtc }).HasFilter("\"PublishedAtUtc\" IS NULL");
    }
}

public sealed class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> b)
    {
        b.ToTable("webhook_endpoints"); b.HasKey(x => x.Id);
        b.Property(x => x.Url).HasMaxLength(2048).IsRequired();
        b.Property(x => x.EventTypes).HasMaxLength(240).IsRequired();
        b.Property(x => x.EncryptedSecret).HasMaxLength(2048).IsRequired();
        b.HasIndex(x => x.IsActive);
        b.HasIndex(x => new { x.CreatedAtUtc, x.Id }).IsDescending().HasFilter("\"IsDeleted\" = false");
    }
}

public sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> b)
    {
        b.ToTable("webhook_deliveries"); b.HasKey(x => x.Id);
        b.Property(x => x.EventType).HasMaxLength(80).IsRequired();
        b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(x => x.LastError).HasMaxLength(500);
        b.HasOne(x => x.WebhookEndpoint).WithMany().HasForeignKey(x => x.WebhookEndpointId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.EventId, x.WebhookEndpointId }).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptAtUtc, x.LeaseUntilUtc });
        b.HasIndex(x => new { x.CreatedAtUtc, x.Id }).IsDescending();
    }
}

public sealed class ConsumedIntegrationEventConfiguration : IEntityTypeConfiguration<ConsumedIntegrationEvent>
{
    public void Configure(EntityTypeBuilder<ConsumedIntegrationEvent> b)
    { b.ToTable("consumed_integration_events"); b.HasKey(x => x.EventId); }
}
