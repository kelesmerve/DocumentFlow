using DocumentFlow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentFlow.Infrastructure.Persistence;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DocumentNumber).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.DocumentNumber).IsUnique();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Category).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.HasIndex(x => new { x.OwnerId, x.CreatedAtUtc });
        builder.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Versions).WithOne(x => x.Document).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}
