using System.Net;
using System.Net.Http.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Ingestion;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

public sealed class BackfillSpellsEndpointTests : IDisposable
{
    private readonly string _root;
    private readonly string _fivetoolsDir;
    private readonly string _canonicalDir;

    public BackfillSpellsEndpointTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "backfill-ep-" + Guid.NewGuid().ToString("N"));
        _fivetoolsDir = Path.Combine(_root, "5etools");
        _canonicalDir = Path.Combine(_root, "canonical");
        Directory.CreateDirectory(Path.Combine(_fivetoolsDir, "spells"));
        Directory.CreateDirectory(_canonicalDir);

        File.WriteAllText(Path.Combine(_fivetoolsDir, "books.json"), """
        { "book": [ { "id": "PHB", "name": "Player's Handbook (2014)", "group": "core", "published": "2014-08-19" } ] }
        """);
        File.WriteAllText(Path.Combine(_fivetoolsDir, "spells", "spells-phb.json"), """
        { "spell": [
          { "name": "Fireball", "source": "PHB", "page": 241, "entries": ["boom"] },
          { "name": "Ray of Sickness", "source": "PHB", "page": 271, "entries": ["ray"], "damageInflict": ["poison"] }
        ] }
        """);
        File.WriteAllText(Path.Combine(_canonicalDir, "phb14.json"), """
        {
          "schemaVersion": "1",
          "book": { "sourceBook": "PHB", "edition": "Edition2014", "fileHash": "", "displayName": "Player's Handbook 2014" },
          "entities": [
            { "id": "phb14.spell.fireball", "type": "Spell", "name": "Fireball", "sourceBook": "PHB",
              "edition": "Edition2014", "page": 242,
              "firstAppearedIn": { "book": "PHB", "edition": "Edition2014", "page": 242 },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": { "entries": [] }, "dataSource": "", "srd": false, "srd52": false,
              "basicRules2024": false, "needsReview": false, "disposition": "Accepted", "keywords": [] }
          ]
        }
        """);
    }

    private async Task<(HttpClient Client, IIngestionTracker Tracker)> BuildClientAsync()
    {
        var tracker = Substitute.For<IIngestionTracker>();
        var registry = new BookSourceRegistry(Path.Combine(_fivetoolsDir, "books.json"));
        var loader = new CanonicalJsonLoader();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(tracker);
        builder.Services.AddSingleton(loader);
        builder.Services.AddSingleton(new CanonicalJsonWriter());
        builder.Services.AddSingleton(new SpellBackfillService(registry, loader, _canonicalDir, _fivetoolsDir));
        // Other endpoints in the group need these services resolvable during metadata inference.
        builder.Services.AddSingleton(Substitute.For<IIngestionQueue>());
        builder.Services.AddSingleton(Substitute.For<IBookDeletionService>());
        builder.Services.AddSingleton<ILogger<RegisterBookRequest>>(NullLogger<RegisterBookRequest>.Instance);
        builder.Services.Configure<AdminOptions>(o => o.ApiKey = "test-key");
        builder.Services.Configure<IngestionOptions>(o => o.BooksPath = Path.GetTempPath());
        builder.Services.Configure<EntityExtractionOptions>(o => o.CanonicalDirectory = _canonicalDir);

        var app = builder.Build();
        app.MapGroup("/admin").MapBooksAdmin();
        await app.StartAsync();
        return (app.GetTestClient(), tracker);
    }

    private static IngestionRecord Record(string? sourceKey) => new()
    {
        Id = 1,
        FilePath = "/tmp/x.pdf",
        FileName = "x.pdf",
        FileHash = "h",
        Version = "5e",
        DisplayName = "Player's Handbook 2014",
        Status = IngestionStatus.JsonIngested,
        FivetoolsSourceKey = sourceKey,
    };

    private sealed record BackfillResponse(List<string> Backfilled, int AlreadyPresent);

    [Fact]
    public async Task Backfill_AppendsGap_ReturnsSummary_AndWritesCanonical()
    {
        var (client, tracker) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Record("PHB"));

        var response = await client.PostAsync("/admin/books/1/backfill-spells", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BackfillResponse>();
        Assert.NotNull(body);
        Assert.Equal(1, body!.AlreadyPresent);
        Assert.Contains("Ray of Sickness", body.Backfilled);

        // Canonical file now contains the backfilled entity.
        var canonical = await File.ReadAllTextAsync(Path.Combine(_canonicalDir, "phb14.json"));
        Assert.Contains("phb14.spell.ray-of-sickness", canonical);
        Assert.Contains("5etools-backfill", canonical);
    }

    [Fact]
    public async Task Backfill_RecordNotFound_Returns404()
    {
        var (client, tracker) = await BuildClientAsync();
        tracker.GetByIdAsync(9999, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);

        var response = await client.PostAsync("/admin/books/9999/backfill-spells", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Backfill_NoSourceKey_Returns400()
    {
        var (client, tracker) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Record(null));

        var response = await client.PostAsync("/admin/books/1/backfill-spells", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
