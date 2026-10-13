using DocumentFlow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentFlow.Infrastructure.Persistence;

public sealed class DocumentTimestampConfiguration : IEntityTypeConfiguration<DocumentTimestamp>
{
    public void Configure(EntityTypeBuilder<DocumentTimestamp> builder)
    {
        builder.ToTable("document_timestamps");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TimestampToken).HasColumnType("text").IsRequired();
        builder.Property(x => x.TimestampAuthority).HasMaxLength(250).IsRequired();
        builder.Property(x => x.SerialNumber).HasMaxLength(100).IsRequired();
        builder.Property(x => x.MessageImprintAlgorithm).HasMaxLength(80).IsRequired();
        builder.Property(x => x.MessageImprint).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(x => x.ProviderType).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Nonce).HasMaxLength(64).IsRequired();
        builder.Property(x => x.GenTimeUtc).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.HasOne(x => x.DocumentSignature).WithOne().HasForeignKey<DocumentTimestamp>(x => x.DocumentSignatureId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.DocumentSignatureId).IsUnique();
        builder.HasIndex(x => x.GenTimeUtc).IsDescending();
    }
}
