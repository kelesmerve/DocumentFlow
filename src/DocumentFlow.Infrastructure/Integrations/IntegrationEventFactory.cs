using System.Text.Json;
using DocumentFlow.Domain;

namespace DocumentFlow.Infrastructure.Integrations;

public static class IntegrationEventFactory
{
    public const string DocumentApproved = "DocumentApproved";
    public const string DocumentSigned = "DocumentSigned";
    public const string MobileSignatureCompleted = "MobileSignatureCompleted";
    public static readonly string[] SupportedTypes = [DocumentApproved, DocumentSigned, MobileSignatureCompleted];

    public static OutboxMessage Create(string eventType, Guid aggregateId, Guid documentId, Guid versionId,
        DateTime occurredAtUtc, Guid? signatureId = null)
    {
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            eventId, eventType, aggregateId, occurredAtUtc, schemaVersion = 1,
            documentId, documentVersionId = versionId, signatureId
        });
        return new OutboxMessage(eventId, eventType, aggregateId, occurredAtUtc, payload);
    }
}
