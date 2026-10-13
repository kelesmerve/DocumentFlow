using DocumentFlow.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentFlow.Infrastructure.Persistence;

public sealed class DocumentFlowDbContext(DbContextOptions<DocumentFlowDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DocumentSignature> DocumentSignatures => Set<DocumentSignature>();
    public DbSet<DocumentTimestamp> DocumentTimestamps => Set<DocumentTimestamp>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("document_number_sequence");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DocumentFlowDbContext).Assembly);
    }
}
