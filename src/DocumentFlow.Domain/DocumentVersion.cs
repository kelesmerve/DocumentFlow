namespace DocumentFlow.Domain;

public sealed class DocumentVersion
{
    private DocumentVersion() { }

    public DocumentVersion(Guid id, Guid documentId, int versionNumber, string originalFileName, string storedFileName, string contentType, long fileSize, string fileHash, Guid uploadedByUserId, DateTime createdAtUtc)
    {
        Id = id;
        DocumentId = documentId;
        VersionNumber = versionNumber;
        OriginalFileName = originalFileName;
        StoredFileName = storedFileName;
        ContentType = contentType;
        FileSize = fileSize;
        FileHash = fileHash;
        UploadedByUserId = uploadedByUserId;
        CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
    }

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public int VersionNumber { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string StoredFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public string FileHash { get; private set; } = string.Empty;
    public Guid UploadedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Document Document { get; private set; } = null!;
    public User UploadedByUser { get; private set; } = null!;
    public ICollection<ApprovalRequest> ApprovalRequests { get; private set; } = new List<ApprovalRequest>();
}
