namespace DocumentFlow.Domain;

public sealed class Document
{
    private Document() { }

    public Document(Guid id, string documentNumber, string title, string? description, string category, Guid ownerId, DateTime createdAtUtc)
    {
        Id = id;
        DocumentNumber = documentNumber;
        Title = title;
        Description = description;
        Category = category;
        OwnerId = ownerId;
        Status = DocumentStatus.Draft;
        CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
    }

    public Guid Id { get; private set; }
    public string DocumentNumber { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public DocumentStatus Status { get; private set; }
    public Guid OwnerId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public User Owner { get; private set; } = null!;
    public ICollection<DocumentVersion> Versions { get; private set; } = new List<DocumentVersion>();

    public void AddVersion(DocumentVersion version, DateTime createdAtUtc)
    {
        if (Status != DocumentStatus.Draft) throw new InvalidOperationException("Only draft documents can receive new versions.");
        Versions.Add(version);
        UpdatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
    }
}
