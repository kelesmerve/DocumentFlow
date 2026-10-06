using DocumentFlow.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentFlow.Infrastructure.Persistence;

public sealed class DocumentFlowDbContext(DbContextOptions<DocumentFlowDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyConfigurationsFromAssembly(typeof(DocumentFlowDbContext).Assembly);
}
