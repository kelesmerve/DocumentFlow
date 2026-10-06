using DocumentFlow.Domain;

namespace DocumentFlow.Application.Documents;

public sealed record DocumentUpload(string FileName, string ContentType, long Length, Stream Content);
public sealed record DocumentVersionSummary(int VersionNumber, string OriginalFileName, string ContentType, long FileSize, Guid UploadedByUserId, DateTime CreatedAtUtc);
public sealed record DocumentSummary(Guid Id, string DocumentNumber, string Title, string? Description, string Category, DocumentStatus Status, Guid OwnerId, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);
public sealed record DocumentDetails(DocumentSummary Document, IReadOnlyList<DocumentVersionSummary> Versions);
public sealed record DocumentPage(IReadOnlyList<DocumentSummary> Items, int Page, int PageSize, int TotalCount);
public sealed record DocumentDownload(Stream Content, string ContentType, string FileName);

public interface IFileStorage
{
    Task<string> SaveAsync(Guid documentId, int versionNumber, string extension, Stream content, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(Guid documentId, int versionNumber, string storedFileName, CancellationToken cancellationToken);
    Task DeleteAsync(Guid documentId, int versionNumber, string storedFileName, CancellationToken cancellationToken);
}

public interface IDocumentService
{
    Task<DocumentDetails> CreateAsync(Guid ownerId, string title, string? description, string category, DocumentUpload upload, CancellationToken cancellationToken);
    Task<DocumentPage> ListAsync(Guid userId, bool isAdmin, int page, int pageSize, CancellationToken cancellationToken);
    Task<DocumentDetails?> GetAsync(Guid documentId, Guid userId, bool isAdmin, CancellationToken cancellationToken);
    Task<DocumentDownload?> DownloadAsync(Guid documentId, int versionNumber, Guid userId, bool isAdmin, CancellationToken cancellationToken);
    Task<DocumentDetails?> AddVersionAsync(Guid documentId, Guid userId, DocumentUpload upload, CancellationToken cancellationToken);
}

public sealed class DocumentInputException(string message) : Exception(message);
