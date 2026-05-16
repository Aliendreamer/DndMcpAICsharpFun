using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Tests.Entities.Deletion;

public class EntityBookDeletionTests
{
    [Fact]
    public async Task Delete_calls_entity_store_delete_and_removes_canonical_json()
    {
        var tracker = Substitute.For<IIngestionTracker>();
        var pdfPath = Path.GetTempFileName();
        var record = new IngestionRecord
        {
            Id = 7,
            DisplayName = "test-book",
            FileHash = "deadbeef",
            FilePath = pdfPath,
            ChunkCount = 10,
            Status = IngestionStatus.JsonIngested,
        };
        tracker.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(record);
        tracker.DeleteAsync(7, Arg.Any<CancellationToken>()).Returns(true);

        var blockStore = Substitute.For<IVectorStoreService>();
        var entityStore = Substitute.For<IEntityVectorStore>();

        var canonicalDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(canonicalDir);
        var canonicalPath = Path.Combine(canonicalDir, "test-book.json");
        await File.WriteAllTextAsync(canonicalPath, "{}");

        var svc = new BookDeletionService(
            tracker,
            blockStore,
            entityStore,
            Options.Create(new EntityIngestionOptions { CanonicalDirectory = canonicalDir }),
            NullLogger<BookDeletionService>.Instance);

        var result = await svc.DeleteBookAsync(7, CancellationToken.None);

        Assert.Equal(DeleteBookResult.Deleted, result);
        await entityStore.Received(1).DeleteByFileHashAsync("deadbeef", Arg.Any<CancellationToken>());
        Assert.False(File.Exists(canonicalPath));

        Directory.Delete(canonicalDir, recursive: true);
        if (File.Exists(pdfPath)) File.Delete(pdfPath);
    }

    [Fact]
    public async Task Delete_clears_block_points_when_book_is_entity_ingested()
    {
        var tracker = Substitute.For<IIngestionTracker>();
        var record = new IngestionRecord
        {
            Id = 8,
            DisplayName = "test-book",
            FileHash = "deadbeef",
            FilePath = Path.GetTempFileName(),
            ChunkCount = 42,
            EntityCount = 3,
            Status = IngestionStatus.EntitiesIngested,
        };
        tracker.GetByIdAsync(8, Arg.Any<CancellationToken>()).Returns(record);

        var blockStore = Substitute.For<IVectorStoreService>();
        var entityStore = Substitute.For<IEntityVectorStore>();
        var canonicalDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(canonicalDir);

        var svc = new BookDeletionService(
            tracker, blockStore, entityStore,
            Options.Create(new EntityIngestionOptions { CanonicalDirectory = canonicalDir }),
            NullLogger<BookDeletionService>.Instance);

        var result = await svc.DeleteBookAsync(8, CancellationToken.None);

        Assert.Equal(DeleteBookResult.Deleted, result);
        await blockStore.Received(1).DeleteBlocksByHashAsync("deadbeef", Arg.Any<CancellationToken>());
        await entityStore.Received(1).DeleteByFileHashAsync("deadbeef", Arg.Any<CancellationToken>());

        Directory.Delete(canonicalDir, recursive: true);
        if (File.Exists(record.FilePath)) File.Delete(record.FilePath);
    }
}
