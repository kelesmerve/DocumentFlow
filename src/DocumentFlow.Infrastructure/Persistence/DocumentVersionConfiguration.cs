using DocumentFlow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentFlow.Infrastructure.Persistence;

public sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("document_versions");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.DocumentId, x.Id });
        builder.Property(x => x.VersionNumber).IsRequired();
        builder.HasIndex(x => new { x.DocumentId, x.VersionNumber }).IsUnique();
        builder.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StoredFileName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(127).IsRequired();
        builder.Property(x => x.FileSize).IsRequired();
        builder.Property(x => x.FileHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.HasIndex(x => x.UploadedByUserId);
        builder.HasOne(x => x.UploadedByUser).WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
