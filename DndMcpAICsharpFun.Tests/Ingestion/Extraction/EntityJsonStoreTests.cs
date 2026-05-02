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
        _store = new EntityJsonStore(Options.Create(new IngestionOptions { BooksPath = _tempRoot }));
    }

    public void Dispose() => Directory.Delete(_tempRoot, recursive: true);

    private static StructuredPage MakePage(int pageNumber, string text = "text") =>
        new(pageNumber, text, [new PageBlock(1, "body", text)]);

    private static ExtractedEntity MakeEntity(string name, int page, bool partial = false) =>
        new(page, "PHB", "Edition2014", partial, "Rule", name,
            new JsonObject { ["description"] = name });

    [Fact]
    public async Task SaveAndLoadRoundtrip()
    {
        var page = MakePage(1, "Big fire.");
        var entity = new ExtractedEntity(1, "PHB", "Edition2014", false, "Spell", "Fireball",
            new JsonObject { ["level"] = 3, ["description"] = "Big fire." });

        await _store.SavePageAsync(bookId: 99, page, [entity]);
        var pages = await _store.LoadAllPagesAsync(99);

        Assert.Single(pages);
        Assert.Single(pages[0].Entities);
        var loaded = pages[0].Entities[0];
        Assert.Equal("Fireball", loaded.Name);
        Assert.Equal(3, loaded.Data["level"]?.GetValue<int>());
        Assert.Equal("Big fire.", pages[0].RawText);
        Assert.Single(pages[0].Blocks);
    }

    [Fact]
    public async Task LoadAllPagesReturnsInPageOrder()
    {
        await _store.SavePageAsync(99, MakePage(3), [MakeEntity("C", 3)]);
        await _store.SavePageAsync(99, MakePage(1), [MakeEntity("A", 1)]);
        await _store.SavePageAsync(99, MakePage(2), [MakeEntity("B", 2)]);

        var pages = await _store.LoadAllPagesAsync(99);

        Assert.Equal(3, pages.Count);
        Assert.Equal("A", pages[0].Entities[0].Name);
        Assert.Equal("B", pages[1].Entities[0].Name);
        Assert.Equal("C", pages[2].Entities[0].Name);
    }

    [Fact]
    public async Task MergePassConcatenatesDescriptionAndDropsDuplicate()
    {
        var partial = new ExtractedEntity(1, "PHB", "Edition2014", true, "Spell", "Fireball",
            new JsonObject { ["description"] = "Big fire" });
        var continuation = new ExtractedEntity(2, "PHB", "Edition2014", false, "Spell", "Fireball",
            new JsonObject { ["description"] = " ball." });

        await _store.SavePageAsync(99, MakePage(1, "Big fire"), [partial]);
        await _store.SavePageAsync(99, MakePage(2, " ball."), [continuation]);

        await _store.RunMergePassAsync(99);

        var pages = await _store.LoadAllPagesAsync(99);
        Assert.Single(pages[0].Entities);
        Assert.Empty(pages[1].Entities);
        Assert.Equal("Big fire ball.", pages[0].Entities[0].Data["description"]?.GetValue<string>());
        Assert.False(pages[0].Entities[0].Partial);
    }

    [Fact]
    public async Task MergePassSetsPageEndOnMergedEntity()
    {
        var partial = new ExtractedEntity(1, "PHB", "Edition2014", true, "Class", "Halfling",
            new JsonObject { ["description"] = "First part" });
        var continuation = new ExtractedEntity(2, "PHB", "Edition2014", false, "Class", "Halfling",
            new JsonObject { ["description"] = " second part." });

        await _store.SavePageAsync(99, MakePage(1), [partial]);
        await _store.SavePageAsync(99, MakePage(2), [continuation]);

        await _store.RunMergePassAsync(99);

        var pages = await _store.LoadAllPagesAsync(99);
        Assert.Equal(2, pages[0].Entities[0].PageEnd);
    }

    [Fact]
    public async Task MergePassSinglePageEntityHasNullPageEnd()
    {
        var entity = MakeEntity("Dragon", 1);
        await _store.SavePageAsync(99, MakePage(1), [entity]);

        await _store.RunMergePassAsync(99);

        var pages = await _store.LoadAllPagesAsync(99);
        Assert.Null(pages[0].Entities[0].PageEnd);
    }

    [Fact]
    public async Task OldBareArrayFormatReturnsEmptyEntities()
    {
        var dir = Path.Combine(_tempRoot, "extracted", "99");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "page_1.json"),
            """[{"page":1,"source_book":"PHB","version":"Edition2014","partial":false,"type":"Rule","name":"Test","data":{}}]""");

        var pages = await _store.LoadAllPagesAsync(99);

        Assert.Single(pages);
        Assert.Empty(pages[0].Entities);
    }

    [Fact]
    public void ListPageFilesReturnsAllFiles()
    {
        var dir = Path.Combine(_tempRoot, "extracted", "99");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "page_1.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "page_2.json"), "{}");

        Assert.Equal(2, _store.ListPageFiles(99).Count());
    }

    [Fact]
    public void DeleteAllPagesRemovesDirectory()
    {
        var dir = Path.Combine(_tempRoot, "extracted", "99");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "page_1.json"), "{}");

        _store.DeleteAllPages(99);

        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public async Task SaveAndLoad_SectionFields_RoundtripCorrectly()
    {
        var page = MakePage(10, "section text");
        var entity = new ExtractedEntity(
            Page: 10, SourceBook: "PHB", Version: "Edition2014",
            Partial: false, Type: "Class", Name: "Wizard",
            Data: new JsonObject { ["description"] = "arcane mage" },
            PageEnd: null,
            SectionTitle: "Wizard",
            SectionStart: 112,
            SectionEnd: 121);

        await _store.SavePageAsync(bookId: 77, page, [entity]);
        var pages = await _store.LoadAllPagesAsync(77);

        Assert.Single(pages);
        var loaded = pages[0].Entities[0];
        Assert.Equal("Wizard", loaded.SectionTitle);
        Assert.Equal(112, loaded.SectionStart);
        Assert.Equal(121, loaded.SectionEnd);
    }

    [Fact]
    public async Task SaveAndLoad_NullSectionFields_RoundtripAsNull()
    {
        var page = MakePage(11);
        var entity = new ExtractedEntity(
            Page: 11, SourceBook: "PHB", Version: "Edition2014",
            Partial: false, Type: "Rule", Name: "Proficiency",
            Data: new JsonObject { ["description"] = "rules text" });

        await _store.SavePageAsync(bookId: 88, page, [entity]);
        var pages = await _store.LoadAllPagesAsync(88);

        var loaded = pages[0].Entities[0];
        Assert.Null(loaded.SectionTitle);
        Assert.Null(loaded.SectionStart);
        Assert.Null(loaded.SectionEnd);
    }
}
