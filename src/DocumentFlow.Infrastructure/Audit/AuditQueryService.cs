using DocumentFlow.Application.Audit;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocumentFlow.Infrastructure.Audit;

public sealed class AuditQueryService(DocumentFlowDbContext db) : IAuditQueryService
{
    private static readonly IReadOnlyDictionary<AuditAction, string> ActionNames = new Dictionary<AuditAction, string>
    {
        [AuditAction.DocumentCreated] = "Belge oluşturuldu",
        [AuditAction.DocumentVersionUploaded] = "Versiyon yüklendi",
        [AuditAction.ApprovalSubmitted] = "Onaya gönderildi",
        [AuditAction.ApprovalApproved] = "Onaylandı",
        [AuditAction.ApprovalRejected] = "Reddedildi",
        [AuditAction.RevisionRequested] = "Revizyon istendi",
        [AuditAction.DocumentDownloaded] = "Dosya indirildi",
        [AuditAction.DocumentSigned] = "Belge imzalandı",
        [AuditAction.TimestampCreated] = "Zaman damgası oluşturuldu",
        [AuditAction.MobileSignatureRequested] = "Mobil imza talep edildi",
        [AuditAction.MobileSignatureApproved] = "Mobil imza onaylandı",
        [AuditAction.MobileSignatureRejected] = "Mobil imza reddedildi",
        [AuditAction.MobileSignatureCompleted] = "Mobil imza tamamlandı",
        [AuditAction.WebhookCreated] = "Webhook eklendi",
        [AuditAction.WebhookEnabled] = "Webhook etkinleştirildi",
        [AuditAction.WebhookDisabled] = "Webhook devre dışı bırakıldı",
        [AuditAction.WebhookDeleted] = "Webhook silindi",
        [AuditAction.WebhookSecretRotated] = "Webhook secret yenilendi"
    };

    public async Task<AuditPage> ListAsync(AuditAction? action, Guid? userId, string? documentNumber, int page, int pageSize, CancellationToken cancellationToken)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Min(Math.Max(page, 1), int.MaxValue / pageSize);
        var query = db.AuditLogs.AsNoTracking();
        if (action.HasValue) query = query.Where(x => x.Action == action.Value);
        if (userId.HasValue) query = query.Where(x => x.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(documentNumber))
        {
            var number = documentNumber.Trim();
            query = query.Where(x => x.Document != null && x.Document.DocumentNumber == number);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rawItems = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.Action,
                x.UserId,
                UserName = x.User.FirstName + " " + x.User.LastName,
                UserEmail = x.User.Email,
                x.DocumentId,
                DocumentNumber = x.Document == null ? null : x.Document.DocumentNumber,
                DocumentTitle = x.Document == null ? null : x.Document.Title,
                x.DocumentVersionId,
                VersionNumber = x.DocumentVersion == null ? (int?)null : x.DocumentVersion.VersionNumber,
                x.ApprovalRequestId,
                x.Description,
                x.CreatedAtUtc
            }).ToListAsync(cancellationToken);

        var items = rawItems.Select(x => new AuditEntry(x.Id, x.Action, ActionNames[x.Action], x.UserId, x.UserName, x.UserEmail,
            x.DocumentId, x.DocumentNumber, x.DocumentTitle, x.DocumentVersionId, x.VersionNumber, x.ApprovalRequestId, x.Description, x.CreatedAtUtc)).ToArray();
        return new AuditPage(items, page, pageSize, totalCount);
    }
}
