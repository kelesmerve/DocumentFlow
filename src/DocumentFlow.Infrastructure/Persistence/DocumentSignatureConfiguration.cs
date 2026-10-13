using DocumentFlow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentFlow.Infrastructure.Persistence;

public sealed class DocumentSignatureConfiguration : IEntityTypeConfiguration<DocumentSignature>
{
    public void Configure(EntityTypeBuilder<DocumentSignature> builder)
    {
        builder.ToTable("document_signatures");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SignatureAlgorithm).HasMaxLength(40).IsRequired();
        builder.Property(x => x.HashAlgorithm).HasMaxLength(40).IsRequired();
        builder.Property(x => x.DocumentHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(x => x.SignatureValue).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.PublicKeyFingerprint).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(x => x.SignedAtUtc).IsRequired();

        builder.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DocumentVersion).WithMany()
            .HasForeignKey(x => new { x.DocumentId, x.DocumentVersionId })
            .HasPrincipalKey(x => new { x.DocumentId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SignedByUser).WithMany().HasForeignKey(x => x.SignedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.DocumentVersionId, x.SignedByUserId }).IsUnique();
        builder.HasIndex(x => x.DocumentId);
        builder.HasIndex(x => x.SignedByUserId);
        builder.HasIndex(x => x.SignedAtUtc).IsDescending();
    }
}
