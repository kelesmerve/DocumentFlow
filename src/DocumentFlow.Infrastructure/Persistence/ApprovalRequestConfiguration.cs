using DocumentFlow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentFlow.Infrastructure.Persistence;

public sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("approval_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.RequestComment).HasMaxLength(2000);
        builder.Property(x => x.DecisionComment).HasMaxLength(2000);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.HasOne(x => x.Document).WithMany(x => x.ApprovalRequests).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DocumentVersion).WithMany(x => x.ApprovalRequests).HasForeignKey(x => new { x.DocumentId, x.DocumentVersionId }).HasPrincipalKey(x => new { x.DocumentId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RequestedByUser).WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AssignedToUser).WithMany().HasForeignKey(x => x.AssignedToUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.AssignedToUserId, x.Status, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.DocumentId, x.CreatedAtUtc });
        builder.HasIndex(x => x.DocumentId).IsUnique().HasFilter("\"Status\" = 'Pending'");
    }
}
