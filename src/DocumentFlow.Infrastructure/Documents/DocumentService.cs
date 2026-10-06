using System.Security.Cryptography;
using DocumentFlow.Application.Documents;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DocumentFlow.Infrastructure.Documents;

public sealed class DocumentService(DocumentFlowDbContext db, IFileStorage storage) : IDocumentService
{
    public const long MaxFileSize = 10 * 1024 * 1024;
    private const string PdfContentType = "application/pdf";
    private const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public async Task<DocumentDetails> CreateAsync(Guid ownerId, string title, string? description, string category, DocumentUpload upload, CancellationToken cancellationToken)
    {
        ValidateMetadata(title, description, category);
        var file = await ReadAndValidateAsync(upload, cancellationToken);
        await using var fileContent = file.Content;
        var documentId = Guid.NewGuid();
        const int versionNumber = 1;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        string? storedName = null;
        try
        {
            storedName = await storage.SaveAsync(documentId, versionNumber, file.Extension, file.Content, cancellationToken);
            var number = await db.Database.SqlQueryRaw<long>("SELECT nextval('document_number_sequence') AS \"Value\"").SingleAsync(cancellationToken);
            var now = DateTime.UtcNow;
            var document = new Document(documentId, $"DOC-{now:yyyy}-{number:D6}", title.Trim(), NormalizeDescription(description), category.Trim(), ownerId, now);
            var version = CreateVersion(documentId, versionNumber, upload, file, storedName, ownerId, now);
            document.AddVersion(version, now);
            db.Documents.Add(document);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToDetails(document, [version]);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (storedName is not null) await storage.DeleteAsync(documentId, versionNumber, storedName, CancellationToken.None);
            throw;
        }
    }

    public async Task<DocumentPage> ListAsync(Guid userId, bool isAdmin, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Min(page, int.MaxValue / pageSize);
        var query = db.Documents.AsNoTracking();
        if (!isAdmin) query = query.Where(x => x.OwnerId == userId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new DocumentSummary(x.Id, x.DocumentNumber, x.Title, x.Description, x.Category, x.Status, x.OwnerId, x.CreatedAtUtc, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
        return new DocumentPage(items, page, pageSize, total);
    }

    public async Task<DocumentDetails?> GetAsync(Guid documentId, Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var document = await AccessibleDocuments(userId, isAdmin)
            .AsNoTracking().Include(x => x.Versions).SingleOrDefaultAsync(x => x.Id == documentId, cancellationToken);
        if (document is null) return null;
        var versions = document.Versions.AsEnumerable();
        if (!isAdmin && document.OwnerId != userId)
        {
            var assignedVersionIds = await db.ApprovalRequests.AsNoTracking().Where(x => x.DocumentId == documentId && x.AssignedToUserId == userId)
                .Select(x => x.DocumentVersionId).ToListAsync(cancellationToken);
            versions = versions.Where(x => assignedVersionIds.Contains(x.Id));
        }
        return ToDetails(document, versions.OrderBy(x => x.VersionNumber).ToArray());
    }

    public async Task<DocumentDownload?> DownloadAsync(Guid documentId, int versionNumber, Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var version = await db.DocumentVersions.AsNoTracking()
            .Where(x => x.DocumentId == documentId && x.VersionNumber == versionNumber &&
                (isAdmin || x.Document.OwnerId == userId || db.ApprovalRequests.Any(a => a.DocumentVersionId == x.Id && a.AssignedToUserId == userId)))
            .Select(x => new { x.VersionNumber, x.OriginalFileName, x.StoredFileName, x.ContentType })
            .SingleOrDefaultAsync(cancellationToken);
        if (version is null) return null;
        var stream = await storage.OpenReadAsync(documentId, version.VersionNumber, version.StoredFileName, cancellationToken);
        return stream is null ? null : new DocumentDownload(stream, version.ContentType, version.OriginalFileName);
    }

    public async Task<DocumentDetails?> AddVersionAsync(Guid documentId, Guid userId, DocumentUpload upload, CancellationToken cancellationToken)
    {
        var file = await ReadAndValidateAsync(upload, cancellationToken);
        await using var fileContent = file.Content;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            string? storedName = null;
            var versionNumber = 0;
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var document = await db.Documents
                    .FromSqlInterpolated($"SELECT * FROM documents WHERE \"Id\" = {documentId} FOR UPDATE")
                    .SingleOrDefaultAsync(cancellationToken);
                if (document is null || document.OwnerId != userId) return null;
                if (document.Status is not (DocumentStatus.Draft or DocumentStatus.RevisionRequested)) throw new DocumentInputException("Versions can only be added to draft or revision-requested documents.");

                versionNumber = (await db.DocumentVersions.Where(x => x.DocumentId == documentId)
                    .MaxAsync(x => (int?)x.VersionNumber, cancellationToken) ?? 0) + 1;
                storedName = await storage.SaveAsync(documentId, versionNumber, file.Extension, file.Content, cancellationToken);
                file.Content.Position = 0;
                var now = DateTime.UtcNow;
                var version = CreateVersion(documentId, versionNumber, upload, file, storedName, userId, now);
                db.DocumentVersions.Add(version);
                document.AddVersion(version, now);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var updatedDocument = await db.Documents.AsNoTracking().SingleAsync(x => x.Id == documentId, cancellationToken);
                var versions = await db.DocumentVersions.AsNoTracking().Where(x => x.DocumentId == documentId)
                    .OrderBy(x => x.VersionNumber).ToListAsync(cancellationToken);
                return ToDetails(updatedDocument, versions);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                if (storedName is not null) await storage.DeleteAsync(documentId, versionNumber, storedName, CancellationToken.None);
                db.ChangeTracker.Clear();
                if (attempt == 2) throw new DocumentInputException("A version could not be allocated because of a concurrent update. Please retry.");
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                if (storedName is not null) await storage.DeleteAsync(documentId, versionNumber, storedName, CancellationToken.None);
                throw;
            }
        }
        throw new DocumentInputException("A version could not be allocated because of a concurrent update. Please retry.");
    }

    private IQueryable<Document> AccessibleDocuments(Guid userId, bool isAdmin)
    {
        var query = db.Documents.AsQueryable();
        return isAdmin ? query : query.Where(x => x.OwnerId == userId || db.ApprovalRequests.Any(a => a.DocumentId == x.Id && a.AssignedToUserId == userId));
    }

    private static async Task<ValidatedUpload> ReadAndValidateAsync(DocumentUpload upload, CancellationToken cancellationToken)
    {
        var originalName = SanitizeOriginalFileName(upload.FileName);
        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        if (extension is not (".pdf" or ".docx")) throw new DocumentInputException("Only PDF and DOCX files are supported.");
        var expectedType = extension == ".pdf" ? PdfContentType : DocxContentType;
        if (!string.Equals(upload.ContentType, expectedType, StringComparison.OrdinalIgnoreCase))
            throw new DocumentInputException("The file extension and content type do not match.");
        if (upload.Length < 1 || upload.Length > MaxFileSize) throw new DocumentInputException("Files must be between 1 byte and 10 MB.");

        var content = new MemoryStream((int)upload.Length);
        try
        {
            await upload.Content.CopyToAsync(content, cancellationToken);
            if (content.Length is < 1 or > MaxFileSize || content.Length != upload.Length)
                throw new DocumentInputException("The uploaded file size is invalid or exceeds 10 MB.");
            content.Position = 0;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(content, cancellationToken));
            content.Position = 0;
            return new ValidatedUpload(originalName, extension, content.Length, hash, content);
        }
        catch { await content.DisposeAsync(); throw; }
    }

    private static string SanitizeOriginalFileName(string input)
    {
        var name = Path.GetFileName((input ?? string.Empty).Replace('\\', '/')).Trim();
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        if (string.IsNullOrWhiteSpace(name)) throw new DocumentInputException("A file name is required.");
        if (name.Length > 255) name = name[^255..];
        return name;
    }

    private static void ValidateMetadata(string title, string? description, string category)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200) throw new DocumentInputException("Title is required and must be at most 200 characters.");
        if (description?.Length > 2000) throw new DocumentInputException("Description must be at most 2000 characters.");
        if (string.IsNullOrWhiteSpace(category) || category.Trim().Length > 100) throw new DocumentInputException("Category is required and must be at most 100 characters.");
    }

    private static string? NormalizeDescription(string? description) => string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static DocumentVersion CreateVersion(Guid documentId, int number, DocumentUpload upload, ValidatedUpload file, string storedName, Guid uploadedBy, DateTime now) =>
        new(Guid.NewGuid(), documentId, number, file.OriginalFileName, storedName, upload.ContentType, file.Length, file.Hash, uploadedBy, now);

    private static DocumentDetails ToDetails(Document document, IEnumerable<DocumentVersion> versions) =>
        new(ToSummary(document), versions.Select(x => new DocumentVersionSummary(x.VersionNumber, x.OriginalFileName, x.ContentType, x.FileSize, x.FileHash, x.UploadedByUserId, x.CreatedAtUtc)).ToArray());

    private static DocumentSummary ToSummary(Document document) => new(document.Id, document.DocumentNumber, document.Title, document.Description, document.Category, document.Status, document.OwnerId, document.CreatedAtUtc, document.UpdatedAtUtc);

    private static bool IsUniqueViolation(DbUpdateException exception) => exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure };

    private sealed record ValidatedUpload(string OriginalFileName, string Extension, long Length, string Hash, MemoryStream Content);
}
