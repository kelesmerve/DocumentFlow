using DocumentFlow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentFlow.Infrastructure.Persistence;

public sealed class MobileSignatureRequestConfiguration : IEntityTypeConfiguration<MobileSignatureRequest>
{
    public void Configure(EntityTypeBuilder<MobileSignatureRequest> builder)
    {
        builder.ToTable("mobile_signature_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FileHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.ExpiresAtUtc).IsRequired();
        builder.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DocumentVersion).WithMany()
            .HasForeignKey(x => new { x.DocumentId, x.DocumentVersionId })
            .HasPrincipalKey(x => new { x.DocumentId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DocumentSignature).WithMany().HasForeignKey(x => x.DocumentSignatureId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DocumentVersionId, x.ManagerId }).IsUnique()
            .HasFilter("\"Status\" = 'Pending'");
        builder.HasIndex(x => new { x.ManagerId, x.Status, x.CreatedAtUtc }).IsDescending(false, false, true);
        builder.HasIndex(x => x.DocumentId);
        builder.HasIndex(x => x.DocumentSignatureId).IsUnique();
    }
}
