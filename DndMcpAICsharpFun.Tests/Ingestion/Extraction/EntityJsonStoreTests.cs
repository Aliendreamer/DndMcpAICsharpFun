using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public sealed class EntityJsonStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly EntityJsonStore _store;

    public EntityJsonStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var opts = Options.Create(new IngestionOptions { BooksPath = _tempRoot });
        _store = new EntityJsonStore(opts);
    }

    public void Dispose() => Directory.Delete(_tempRoot, recursive: true);

    [Fact]
    public async Task SaveAndLoadRoundtrip()
    {
        var entity = new ExtractedEntity(1, "PHB", "Edition2014", false, "Spell", "Fireball",
            new JsonObject { ["level"] = 3, ["description"] = "Big fire." });

        await _store.SavePageAsync(bookId: 99, pageNumber: 1, [entity]);

        var pages = await _store.LoadAllPagesAsync(bookId: 99);

        Assert.Single(pages);
        Assert.Single(pages[0]);
        var loaded = pages[0][0];
        Assert.Equal("Fireball", loaded.Name);
        Assert.Equal(3, loaded.Data["level"]?.GetValue<int>());
    }

    [Fact]
    public async Task LoadAllPagesReturnsInPageOrder()
    {
        await _store.SavePageAsync(99, 3, [MakeEntity("C", 3)]);
        await _store.SavePageAsync(99, 1, [MakeEntity("A", 1)]);
        await _store.SavePageAsync(99, 2, [MakeEntity("B", 2)]);

        var pages = await _store.LoadAllPagesAsync(99);

        Assert.Equal(3, pages.Count);
        Assert.Equal("A", pages[0][0].Name);
        Assert.Equal("B", pages[1][0].Name);
        Assert.Equal("C", pages[2][0].Name);
    }

    [Fact]
    public async Task MergePassConcatenatesDescriptionAndDropsDuplicate()
    {
        var partialEntity = new ExtractedEntity(1, "PHB", "Edition2014", true, "Spell", "Fireball",
            new JsonObject { ["description"] = "Big fire" });
        var continuedEntity = new ExtractedEntity(2, "PHB", "Edition2014", false, "Spell", "Fireball",
            new JsonObject { ["description"] = " ball." });

        await _store.SavePageAsync(99, 1, [partialEntity]);
        await _store.SavePageAsync(99, 2, [continuedEntity]);

        await _store.RunMergePassAsync(99);

        var pages = await _store.LoadAllPagesAsync(99);
        // page 1 entity merged; page 2 entity removed
        Assert.Single(pages[0]);
        Assert.Empty(pages[1]);
        Assert.Equal("Big fire ball.", pages[0][0].Data["description"]?.GetValue<string>());
        Assert.False(pages[0][0].Partial);
    }

    [Fact]
    public void ListPageFilesReturnsAllFiles()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "extracted", "99"));
        File.WriteAllText(Path.Combine(_tempRoot, "extracted", "99", "page_1.json"), "[]");
        File.WriteAllText(Path.Combine(_tempRoot, "extracted", "99", "page_2.json"), "[]");

        var files = _store.ListPageFiles(99).ToList();
        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void DeleteAllPagesRemovesDirectory()
    {
        var dir = Path.Combine(_tempRoot, "extracted", "99");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "page_1.json"), "[]");

        _store.DeleteAllPages(99);

        Assert.False(Directory.Exists(dir));
    }

    private static ExtractedEntity MakeEntity(string name, int page) =>
        new(page, "PHB", "Edition2014", false, "Rule", name, new JsonObject { ["description"] = name });
}
