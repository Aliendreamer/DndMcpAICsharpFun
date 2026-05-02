using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Tests.Infrastructure.Tracking;

public sealed class SqliteIngestionTrackerTests : IDisposable
{
    private readonly TrackerFixture _fixture = new();
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task CreateAsync_AssignsId_AndReturnsRecord()
    {
        var tracker = _fixture.CreateTracker();
        var record = TrackerFixture.SampleRecord();

        var created = await tracker.CreateAsync(record);

        Assert.True(created.Id > 0);
        Assert.Equal("Player's Handbook", created.DisplayName);
        Assert.Equal(IngestionStatus.Pending, created.Status);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsRecord()
    {
        var writer = _fixture.CreateTracker();
        var created = await writer.CreateAsync(TrackerFixture.SampleRecord());

        var reader = _fixture.CreateTracker();
        var found = await reader.GetByIdAsync(created.Id);

        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
        Assert.Equal("Player's Handbook", found.DisplayName);
    }

    [Fact]
    public async Task GetByIdAsync_MissingId_ReturnsNull()
    {
        var tracker = _fixture.CreateTracker();

        var result = await tracker.GetByIdAsync(99999);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllRecords()
    {
        var writer = _fixture.CreateTracker();
        await writer.CreateAsync(TrackerFixture.SampleRecord());
        var dmg = TrackerFixture.SampleRecord();
        dmg.DisplayName = "DMG";
        await writer.CreateAsync(dmg);

        var reader = _fixture.CreateTracker();
        var all = await reader.GetAllAsync();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task MarkHashAsync_SetsProcessingAndHash()
    {
        var writer = _fixture.CreateTracker();
        var created = await writer.CreateAsync(TrackerFixture.SampleRecord());

        var updater = _fixture.CreateTracker();
        await updater.MarkHashAsync(created.Id, "abc123def456");

        var reader = _fixture.CreateTracker();
        var updated = await reader.GetByIdAsync(created.Id);
        Assert.NotNull(updated);
        Assert.Equal("abc123def456", updated.FileHash);
        Assert.Equal(IngestionStatus.Processing, updated.Status);
    }

    [Fact]
    public async Task MarkJsonIngestedAsync_SetsStatusAndChunkCount()
    {
        var writer = _fixture.CreateTracker();
        var created = await writer.CreateAsync(TrackerFixture.SampleRecord());

        var updater = _fixture.CreateTracker();
        await updater.MarkJsonIngestedAsync(created.Id, chunkCount: 42);

        var reader = _fixture.CreateTracker();
        var updated = await reader.GetByIdAsync(created.Id);
        Assert.NotNull(updated);
        Assert.Equal(IngestionStatus.JsonIngested, updated.Status);
        Assert.Equal(42, updated.ChunkCount);
    }

    [Fact]
    public async Task MarkFailedAsync_SetsStatusAndError()
    {
        var writer = _fixture.CreateTracker();
        var created = await writer.CreateAsync(TrackerFixture.SampleRecord());

        var updater = _fixture.CreateTracker();
        await updater.MarkFailedAsync(created.Id, "LLM timeout");

        var reader = _fixture.CreateTracker();
        var updated = await reader.GetByIdAsync(created.Id);
        Assert.NotNull(updated);
        Assert.Equal(IngestionStatus.Failed, updated.Status);
        Assert.Equal("LLM timeout", updated.Error);
    }

    [Fact]
    public async Task ResetForReingestionAsync_ResetsStatusToPending()
    {
        var writer = _fixture.CreateTracker();
        var created = await writer.CreateAsync(TrackerFixture.SampleRecord());
        var failer = _fixture.CreateTracker();
        await failer.MarkFailedAsync(created.Id, "some error");

        var resetter = _fixture.CreateTracker();
        await resetter.ResetForReingestionAsync(created.Id);

        var reader = _fixture.CreateTracker();
        var updated = await reader.GetByIdAsync(created.Id);
        Assert.NotNull(updated);
        Assert.Equal(IngestionStatus.Pending, updated.Status);
        Assert.Null(updated.Error);
        Assert.Null(updated.ChunkCount);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecord()
    {
        var writer = _fixture.CreateTracker();
        var created = await writer.CreateAsync(TrackerFixture.SampleRecord());

        var deleter = _fixture.CreateTracker();
        var deleted = await deleter.DeleteAsync(created.Id);

        var reader = _fixture.CreateTracker();
        var found = await reader.GetByIdAsync(created.Id);
        Assert.True(deleted);
        Assert.Null(found);
    }
}
