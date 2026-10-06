using DocumentFlow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentFlow.Infrastructure.Persistence;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Email).HasMaxLength(320).IsRequired();
        builder.HasIndex(x => x.Email).IsUnique();
        builder.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.IsActive).IsRequired(); builder.Property(x => x.CreatedAtUtc).IsRequired();
    }
}
