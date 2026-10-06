using DocumentFlow.Infrastructure.Storage;

namespace DocumentFlow.Application.Tests;

public sealed class LocalFileStorageTests
{
    [Fact]
    public async Task Versions_are_stored_separately_and_untrusted_file_names_are_not_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"documentflow-storage-test-{Guid.NewGuid():N}");
        try
        {
            var storage = new LocalFileStorage(root);
            var documentId = Guid.NewGuid();
            var first = await storage.SaveAsync(documentId, 1, ".pdf", new MemoryStream([1, 2]), CancellationToken.None);
            var second = await storage.SaveAsync(documentId, 2, ".pdf", new MemoryStream([3, 4]), CancellationToken.None);
            Assert.NotEqual(first, second);
            await using var firstStream = await storage.OpenReadAsync(documentId, 1, first, CancellationToken.None);
            await using var secondStream = await storage.OpenReadAsync(documentId, 2, second, CancellationToken.None);
            Assert.NotNull(firstStream);
            Assert.NotNull(secondStream);
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync(documentId, 1, "..\\..\\secret.pdf", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
