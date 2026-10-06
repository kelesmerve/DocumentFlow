using DocumentFlow.Application.Approvals;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DocumentFlow.Infrastructure.Approvals;

public sealed class ApprovalService(DocumentFlowDbContext db) : IApprovalService
{
    public async Task<IReadOnlyList<ManagerSummary>> ListManagersAsync(CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().Where(x => x.IsActive && x.Role == UserRole.Manager)
            .OrderBy(x => x.FirstName).ThenBy(x => x.LastName)
            .Select(x => new ManagerSummary(x.Id, x.FirstName, x.LastName, x.Email)).ToListAsync(cancellationToken);

    public async Task<ApprovalPage> ListAsync(Guid userId, bool isAdmin, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100); page = Math.Min(page, int.MaxValue / pageSize);
        var query = db.ApprovalRequests.AsNoTracking();
        if (!isAdmin) query = query.Where(x => x.AssignedToUserId == userId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new ApprovalSummary(x.Id, x.DocumentId, x.Document.DocumentNumber, x.Document.Title, x.Document.Category,
                x.DocumentVersion.VersionNumber, x.RequestedByUser.FirstName + " " + x.RequestedByUser.LastName, x.RequestedByUser.Email,
                x.RequestComment, x.Status, x.CreatedAtUtc)).ToListAsync(cancellationToken);
        return new ApprovalPage(rows, page, pageSize, total);
    }

    public async Task<ApprovalDetails?> GetAsync(Guid id, Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var request = await db.ApprovalRequests.AsNoTracking().Where(x => x.Id == id && (isAdmin || x.AssignedToUserId == userId))
            .Select(x => new ApprovalDetails(
                new ApprovalSummary(x.Id, x.DocumentId, x.Document.DocumentNumber, x.Document.Title, x.Document.Category,
                    x.DocumentVersion.VersionNumber, x.RequestedByUser.FirstName + " " + x.RequestedByUser.LastName, x.RequestedByUser.Email,
                    x.RequestComment, x.Status, x.CreatedAtUtc), x.Document.Description ?? string.Empty,
                x.DocumentVersion.OriginalFileName, x.DocumentVersion.ContentType, x.DocumentVersion.FileSize, x.DocumentVersion.FileHash,
                x.AssignedToUser.FirstName + " " + x.AssignedToUser.LastName, x.DecisionComment, x.DecidedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
        return request;
    }

    public async Task<ApprovalDetails> SubmitAsync(Guid documentId, Guid ownerId, SubmitApprovalInput input, CancellationToken cancellationToken)
    {
        if (input.Comment?.Length > 2000) throw new ApprovalInputException("Comment must be at most 2000 characters.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var document = await db.Documents.FromSqlInterpolated($"SELECT * FROM documents WHERE \"Id\" = {documentId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        if (document is null || document.OwnerId != ownerId) throw new ApprovalInputException("Document was not found or is not owned by the current user.");
        if (document.Status != DocumentStatus.Draft) throw new ApprovalInputException("Only draft documents can be submitted for approval.");
        var manager = await db.Users.SingleOrDefaultAsync(x => x.Id == input.ManagerId, cancellationToken);
        if (manager is null || !manager.IsActive || manager.Role != UserRole.Manager) throw new ApprovalInputException("The selected user is not an active manager.");
        if (manager.Id == ownerId) throw new ApprovalInputException("A document cannot be assigned to its owner.");
        var version = await db.DocumentVersions.Where(x => x.DocumentId == documentId).OrderByDescending(x => x.VersionNumber).FirstOrDefaultAsync(cancellationToken);
        if (version is null) throw new ApprovalInputException("A document must have a version before it can be submitted.");
        var now = DateTime.UtcNow;
        var request = new ApprovalRequest(Guid.NewGuid(), documentId, version.Id, ownerId, manager.Id, input.Comment, now);
        document.SubmitForApproval(now);
        db.ApprovalRequests.Add(request);
        try { await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ApprovalInputException("This document already has a pending approval request."); }
        return (await GetAsync(request.Id, manager.Id, false, cancellationToken))!;
    }

    public async Task<ApprovalDetails?> DecideAsync(Guid id, Guid managerId, ApprovalStatus decision, string? comment, CancellationToken cancellationToken)
    {
        if (comment?.Length > 2000) throw new ApprovalInputException("Comment must be at most 2000 characters.");
        if (decision is ApprovalStatus.Rejected or ApprovalStatus.RevisionRequested && string.IsNullOrWhiteSpace(comment))
            throw new ApprovalInputException("A decision comment is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var request = await db.ApprovalRequests.FromSqlInterpolated($"SELECT * FROM approval_requests WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        if (request is null || request.AssignedToUserId != managerId) return null;
        var document = await db.Documents.FromSqlInterpolated($"SELECT * FROM documents WHERE \"Id\" = {request.DocumentId} FOR UPDATE").SingleAsync(cancellationToken);
        if (request.Status != ApprovalStatus.Pending || document.Status != DocumentStatus.PendingApproval)
            throw new ApprovalInputException("This approval is no longer pending.");
        var now = DateTime.UtcNow;
        request.Decide(decision, comment, now);
        document.ApplyDecision(decision switch { ApprovalStatus.Approved => DocumentStatus.Approved, ApprovalStatus.Rejected => DocumentStatus.Rejected, _ => DocumentStatus.RevisionRequested }, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, managerId, false, cancellationToken);
    }

    public async Task<bool> CanAccessDocumentAsync(Guid documentId, Guid userId, bool isAdmin, CancellationToken cancellationToken) =>
        isAdmin || await db.Documents.AnyAsync(x => x.Id == documentId && x.OwnerId == userId, cancellationToken) ||
        await db.ApprovalRequests.AnyAsync(x => x.DocumentId == documentId && x.AssignedToUserId == userId, cancellationToken);

    public async Task<IReadOnlyList<WorkflowEvent>?> GetTimelineAsync(Guid documentId, Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        if (!await CanAccessDocumentAsync(documentId, userId, isAdmin, cancellationToken)) return null;
        var document = await db.Documents.AsNoTracking().Include(x => x.Owner).Include(x => x.Versions).ThenInclude(x => x.UploadedByUser)
            .SingleOrDefaultAsync(x => x.Id == documentId, cancellationToken);
        if (document is null) return null;
        var events = new List<WorkflowEvent> { new("created", "Belge oluşturuldu", $"{document.Owner.FirstName} {document.Owner.LastName}", null, null, document.CreatedAtUtc) };
        events.AddRange(document.Versions.Select(v => new WorkflowEvent("version", $"v{v.VersionNumber} yüklendi", $"{v.UploadedByUser.FirstName} {v.UploadedByUser.LastName}", v.OriginalFileName, v.VersionNumber, v.CreatedAtUtc)));
        var approvals = await db.ApprovalRequests.AsNoTracking().Where(x => x.DocumentId == documentId)
            .Include(x => x.DocumentVersion).Include(x => x.RequestedByUser).Include(x => x.AssignedToUser).OrderBy(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
        foreach (var approval in approvals)
        {
            events.Add(new WorkflowEvent("submitted", $"v{approval.DocumentVersion.VersionNumber} onaya gönderildi", $"{approval.RequestedByUser.FirstName} {approval.RequestedByUser.LastName} → {approval.AssignedToUser.FirstName} {approval.AssignedToUser.LastName}", approval.RequestComment, approval.DocumentVersion.VersionNumber, approval.CreatedAtUtc));
            if (approval.DecidedAtUtc is { } decidedAt)
                events.Add(new WorkflowEvent("decision", approval.Status switch { ApprovalStatus.Approved => "Onaylandı", ApprovalStatus.Rejected => "Reddedildi", _ => "Revizyon istendi" }, $"{approval.AssignedToUser.FirstName} {approval.AssignedToUser.LastName}", approval.DecisionComment, approval.DocumentVersion.VersionNumber, decidedAt));
        }
        return events.OrderBy(x => x.AtUtc).ToArray();
    }
}
