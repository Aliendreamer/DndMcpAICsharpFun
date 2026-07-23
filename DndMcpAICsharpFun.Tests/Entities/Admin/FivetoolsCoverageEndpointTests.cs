using System.Net;
using System.Net.Http.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Ingestion;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

/// <summary>
/// Task 6 surface (a): <c>GET /admin/books/{id}/coverage</c> — mirrors
/// <see cref="EntityBackfillEndpointTests"/>'s harness (the same replicated
/// <see cref="IReadOnlyDictionary{EntityType, EntityBackfillService}"/> array) but ALSO wires
/// <see cref="AdminApiKeyMiddleware"/> manually (mirrors <c>NeedsReviewEndpointsTests</c>) so the
/// admin-gate (401 without the key) is actually exercised — the EntityBackfillEndpointTests harness
/// does not wire that middleware, so it cannot prove the gate for this new read-only endpoint.
/// </summary>
public sealed class FivetoolsCoverageEndpointTests : IDisposable
{
    private const string ValidKey = "test-admin-key";

    private readonly string _root;
    private readonly string _fivetoolsDir;
    private readonly string _canonicalDir;

    public FivetoolsCoverageEndpointTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fivetools-coverage-ep-" + Guid.NewGuid().ToString("N"));
        _fivetoolsDir = Path.Combine(_root, "5etools");
        _canonicalDir = Path.Combine(_root, "canonical");
        Directory.CreateDirectory(Path.Combine(_fivetoolsDir, "bestiary"));
        Directory.CreateDirectory(_canonicalDir);

        File.WriteAllText(Path.Combine(_fivetoolsDir, "books.json"), """
        { "book": [ { "id": "MM", "name": "Monster Manual (2014)", "group": "core", "published": "2014-09-30" } ] }
        """);

        File.WriteAllText(Path.Combine(_fivetoolsDir, "bestiary", "bestiary-mm.json"), """
        { "monster": [
          { "name": "Goblin", "source": "MM", "page": 166, "size": ["S"], "type": "humanoid", "str": 8, "cr": "1/4" },
          { "name": "Bullywug", "source": "MM", "page": 35, "size": ["M"], "type": "humanoid", "str": 12, "cr": "1/4" }
        ] }
        """);

        File.WriteAllText(Path.Combine(_canonicalDir, "mm14.json"), """
        {
          "schemaVersion": "1",
          "book": { "sourceBook": "MM", "edition": "Edition2014", "fileHash": "", "displayName": "Monster Manual 2014" },
          "entities": [
            { "id": "mm14.monster.goblin", "type": "Monster", "name": "Goblin", "sourceBook": "MM",
              "edition": "Edition2014", "page": 166,
              "firstAppearedIn": { "book": "MM", "edition": "Edition2014", "page": 166 },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": { "str": 8 }, "dataSource": "", "srd": false, "srd52": false,
              "basicRules2024": false, "needsReview": false, "disposition": "Accepted", "keywords": [] }
          ]
        }
        """);
    }

    private async Task<HttpClient> BuildClientAsync(IIngestionTracker tracker)
    {
        var registry = new BookSourceRegistry(Path.Combine(_fivetoolsDir, "books.json"));
        var loader = new CanonicalJsonLoader();

        IFivetoolsBackfillProvider[] providers =
            {
                new MonsterBackfillProvider(), new SpellBackfillProvider(), new MagicItemBackfillProvider(), new GodBackfillProvider(),
                new FeatBackfillProvider(), new BackgroundBackfillProvider(), new ConditionBackfillProvider(),
                new TrapBackfillProvider(), new DiseasePoisonBackfillProvider(), new VehicleBackfillProvider(),
                new ItemBackfillProvider(), new WeaponBackfillProvider(), new ArmorBackfillProvider(),
            };
        IReadOnlyDictionary<EntityType, EntityBackfillService> backfillServices = providers.ToDictionary(
            p => p.Type,
            p => new EntityBackfillService(p, registry, loader, _canonicalDir, _fivetoolsDir));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(tracker);
        builder.Services.AddSingleton(loader);
        builder.Services.AddSingleton(new CanonicalJsonWriter());
        builder.Services.AddSingleton(backfillServices);
        builder.Services.AddSingleton(new FivetoolsCoverageService(backfillServices, _fivetoolsDir));
        // Other endpoints in the group need these services resolvable during metadata inference.
        builder.Services.AddSingleton(Substitute.For<IIngestionQueue>());
        builder.Services.AddSingleton(Substitute.For<IBookDeletionService>());
        builder.Services.AddSingleton<ILogger<RegisterBookRequest>>(Microsoft.Extensions.Logging.Abstractions.NullLogger<RegisterBookRequest>.Instance);
        builder.Services.Configure<AdminOptions>(o => o.ApiKey = ValidKey);
        builder.Services.Configure<IngestionOptions>(o => o.BooksPath = Path.GetTempPath());
        builder.Services.Configure<EntityExtractionOptions>(o => o.CanonicalDirectory = _canonicalDir);

        var app = builder.Build();

        // Wire AdminApiKeyMiddleware on /admin paths — mirrors MapAdminMiddleware() in Program.cs.
        app.UseWhen(
            static ctx => ctx.Request.Path.StartsWithSegments("/admin"),
            static branch => branch.UseMiddleware<AdminApiKeyMiddleware>());

        app.MapGroup("/admin").MapBooksAdmin();
        await app.StartAsync();
        return app.GetTestClient();
    }

    private static IngestionRecord Record(int id, string displayName, string? sourceKey) => new()
    {
        Id = id,
        FilePath = "/tmp/x.pdf",
        FileName = "x.pdf",
        FileHash = "h",
        Version = "5e",
        DisplayName = displayName,
        Status = IngestionStatus.JsonIngested,
        FivetoolsSourceKey = sourceKey,
    };

    [Fact]
    public async Task Coverage_WithoutApiKey_Returns401()
    {
        var tracker = Substitute.For<IIngestionTracker>();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Record(1, "Monster Manual 2014", "MM"));
        var client = await BuildClientAsync(tracker);

        var response = await client.GetAsync("/admin/books/1/coverage");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the AdminApiKeyMiddleware must reject requests without X-Admin-Api-Key");
    }

    [Fact]
    public async Task Coverage_WithWrongApiKey_Returns401()
    {
        var tracker = Substitute.For<IIngestionTracker>();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Record(1, "Monster Manual 2014", "MM"));
        var client = await BuildClientAsync(tracker);
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", "wrong-key");

        var response = await client.GetAsync("/admin/books/1/coverage");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Coverage_WithCorrectApiKey_ReturnsBookCoverageWithNamedGaps()
    {
        var tracker = Substitute.For<IIngestionTracker>();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Record(1, "Monster Manual 2014", "MM"));
        var client = await BuildClientAsync(tracker);
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", ValidKey);

        var response = await client.GetAsync("/admin/books/1/coverage");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BookCoverage>();
        body.Should().NotBeNull();
        body!.SourceKey.Should().Be("MM");

        var monster = body.PerType.Single(t => t.Type == EntityType.Monster);
        monster.RosterCount.Should().Be(2);
        monster.Present.Should().Be(1);
        monster.MissingCount.Should().Be(1);
        monster.MissingNames.Should().Contain("Bullywug");
    }

    [Fact]
    public async Task Coverage_BookNotFound_Returns404()
    {
        var tracker = Substitute.For<IIngestionTracker>();
        tracker.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);
        var client = await BuildClientAsync(tracker);
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", ValidKey);

        var response = await client.GetAsync("/admin/books/99/coverage");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}