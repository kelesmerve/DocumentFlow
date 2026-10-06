using DocumentFlow.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentFlow.Infrastructure.Persistence;

public sealed class DocumentFlowDbContext(DbContextOptions<DocumentFlowDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("document_number_sequence");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DocumentFlowDbContext).Assembly);
    }
}
