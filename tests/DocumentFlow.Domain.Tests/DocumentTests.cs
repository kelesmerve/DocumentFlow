using DocumentFlow.Domain;

namespace DocumentFlow.Domain.Tests;

public sealed class DocumentTests
{
    [Fact]
    public void New_document_starts_as_draft_and_tracks_latest_version_time()
    {
        var ownerId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var document = new Document(Guid.NewGuid(), "DOC-2026-000001", "Policy", null, "HR", ownerId, now);
        var version = new DocumentVersion(Guid.NewGuid(), document.Id, 1, "policy.pdf", "abcd.pdf", "application/pdf", 4, new string('A', 64), ownerId, now);

        document.AddVersion(version, now.AddMinutes(1));

        Assert.Equal(DocumentStatus.Draft, document.Status);
        Assert.Single(document.Versions);
        Assert.Equal(now.AddMinutes(1), document.UpdatedAtUtc);
    }
}
