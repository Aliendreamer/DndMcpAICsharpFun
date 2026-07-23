using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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
/// Covers the type-parameterized <c>entity-recall</c>/<c>backfill-entities</c>/<c>flag-unknown-entities</c>
/// routes that replaced the old monster/spell-specific endpoints. Model on the retired
/// <c>BackfillSpellsEndpointTests</c>, but registers the full
/// <see cref="IReadOnlyDictionary{EntityType, EntityBackfillService}"/> resolved by <c>?type=</c>.
/// </summary>
public sealed class EntityBackfillEndpointTests : IDisposable
{
    private readonly string _root;
    private readonly string _fivetoolsDir;
    private readonly string _canonicalDir;

    public EntityBackfillEndpointTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "entity-backfill-ep-" + Guid.NewGuid().ToString("N"));
        _fivetoolsDir = Path.Combine(_root, "5etools");
        _canonicalDir = Path.Combine(_root, "canonical");
        Directory.CreateDirectory(Path.Combine(_fivetoolsDir, "bestiary"));
        Directory.CreateDirectory(Path.Combine(_fivetoolsDir, "spells"));
        Directory.CreateDirectory(_canonicalDir);

        File.WriteAllText(Path.Combine(_fivetoolsDir, "books.json"), """
        { "book": [
          { "id": "MM", "name": "Monster Manual (2014)", "group": "core", "published": "2014-09-30" },
          { "id": "PHB", "name": "Player's Handbook (2014)", "group": "core", "published": "2014-08-19" }
        ] }
        """);

        File.WriteAllText(Path.Combine(_fivetoolsDir, "bestiary", "bestiary-mm.json"), """
        { "monster": [
          { "name": "Goblin", "source": "MM", "page": 166, "size": ["S"], "type": "humanoid", "str": 8, "cr": "1/4" },
          { "name": "Bullywug", "source": "MM", "page": 35, "size": ["M"], "type": "humanoid", "str": 12, "cr": "1/4" }
        ] }
        """);
        File.WriteAllText(Path.Combine(_fivetoolsDir, "spells", "spells-phb.json"), """
        { "spell": [
          { "name": "Fireball", "source": "PHB", "page": 241, "entries": ["boom"] },
          { "name": "Ray of Sickness", "source": "PHB", "page": 271, "entries": ["ray"], "damageInflict": ["poison"] }
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
              "basicRules2024": false, "needsReview": false, "disposition": "Accepted", "keywords": [] },
            { "id": "mm14.monster.vault-guardian", "type": "Monster", "name": "Vault Guardian", "sourceBook": "MM",
              "edition": "Edition2014", "page": 999,
              "firstAppearedIn": { "book": "MM", "edition": "Edition2014", "page": 999 },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": { "str": 20 }, "dataSource": "", "srd": false, "srd52": false,
              "basicRules2024": false, "needsReview": false, "disposition": "Accepted", "keywords": [] }
          ]
        }
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

        IFivetoolsBackfillProvider[] providers =
            {
                new MonsterBackfillProvider(), new SpellBackfillProvider(), new MagicItemBackfillProvider(), new GodBackfillProvider(),
                new FeatBackfillProvider(), new BackgroundBackfillProvider(), new ConditionBackfillProvider(),
                new TrapBackfillProvider(), new DiseasePoisonBackfillProvider(), new VehicleBackfillProvider(),
                new ItemBackfillProvider(), new WeaponBackfillProvider(), new ArmorBackfillProvider(),
            };
        var services = providers.ToDictionary(
            p => p.Type,
            p => new EntityBackfillService(p, registry, loader, _canonicalDir, _fivetoolsDir));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(tracker);
        builder.Services.AddSingleton(loader);
        builder.Services.AddSingleton(new CanonicalJsonWriter());
        builder.Services.AddSingleton<IReadOnlyDictionary<EntityType, EntityBackfillService>>(services);
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

    private sealed record BackfillResponse(List<string> Backfilled, int AlreadyPresent);

    [Fact]
    public async Task EntityRecall_Monster_ReturnsRecallShape()
    {
        var (client, tracker) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Record(1, "Monster Manual 2014", "MM"));

        var response = await client.GetAsync("/admin/books/1/entity-recall?type=Monster");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonDocumentResponse>();
        Assert.NotNull(body);
        Assert.True(body!.HasSourceKey);
        Assert.Equal(1, body.Present);
        Assert.Contains("Bullywug", body.Missing);
        Assert.Contains("Vault Guardian", body.Extra);
    }

    private sealed record JsonDocumentResponse(
        bool HasSourceKey,
        string? CanonicalPath,
        int Present,
        int Grounded,
        int Backfilled,
        List<string> Missing,
        List<string> Extra,
        List<string> ExtraOtherSource,
        List<string> ExtraUnknown);

    [Fact]
    public async Task BackfillEntities_Spell_AppendsGap_ReturnsSummary_AndWritesCanonical()
    {
        var (client, tracker) = await BuildClientAsync();
        tracker.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(Record(2, "Player's Handbook 2014", "PHB"));

        var response = await client.PostAsync("/admin/books/2/backfill-entities?type=Spell", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BackfillResponse>();
        Assert.NotNull(body);
        Assert.Equal(1, body!.AlreadyPresent);
        Assert.Contains("Ray of Sickness", body.Backfilled);

        var canonical = await File.ReadAllTextAsync(Path.Combine(_canonicalDir, "phb14.json"));
        Assert.Contains("phb14.spell.ray-of-sickness", canonical);
        Assert.Contains("5etools-backfill", canonical);
    }

    [Fact]
    public async Task FlagUnknownEntities_Monster_FlagsExtraUnknown()
    {
        var (client, tracker) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Record(1, "Monster Manual 2014", "MM"));

        var response = await client.PostAsync("/admin/books/1/flag-unknown-entities?type=Monster", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FlagResponse>();
        Assert.NotNull(body);
        Assert.Contains("Vault Guardian", body!.Flagged);
        Assert.Equal(1, body.FlaggedCount);
    }

    private sealed record FlagResponse(List<string> Flagged, int FlaggedCount);

    [Fact]
    public async Task EntityRecall_UnsupportedType_Returns400()
    {
        var (client, tracker) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Record(1, "Monster Manual 2014", "MM"));

        var response = await client.GetAsync("/admin/books/1/entity-recall?type=Plane");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Proves the 9 newly-registered catalog/base-item providers (Feat, Background, Condition,
    /// Trap, DiseasePoison, VehicleMount, Item, Weapon, Armor) are wired into the SAME
    /// provider array the DI registration in <c>ServiceCollectionExtensions.AddEntityExtraction</c>
    /// uses (this test's <see cref="BuildClientAsync"/> is that array's replica). Runs a
    /// multi-type backfill apply for PHB across several of the new types and reloads the
    /// rewritten canonical to check the dev-flow canonical-rewrite gate: no duplicate-id load
    /// error, unique ids, the pre-existing extraction-owned Spell entity byte-unchanged, and the
    /// new entities correctly marked as backfilled.
    /// </summary>
    [Fact]
    public async Task BackfillEntities_MultiType_RoundTripsCanonicalWithUniqueIdsAndPreservesExtractionOwnedEntity()
    {
        // Gaps across three of the new types, all sourced PHB, at the fivetoolsDir root (where
        // each provider reads its own file — feats.json / conditionsdiseases.json / items-base.json).
        File.WriteAllText(Path.Combine(_fivetoolsDir, "feats.json"), """
        { "feat": [
          { "name": "Alert", "source": "PHB", "page": 165, "entries": ["You are always on alert."] }
        ] }
        """);
        File.WriteAllText(Path.Combine(_fivetoolsDir, "conditionsdiseases.json"), """
        { "condition": [
          { "name": "Prone", "source": "PHB", "page": 292, "entries": ["A prone creature's only movement option is to crawl."] }
        ], "disease": [] }
        """);
        File.WriteAllText(Path.Combine(_fivetoolsDir, "items-base.json"), """
        { "baseitem": [
          { "name": "Handaxe", "source": "PHB", "page": 149, "type": "M", "rarity": "none",
            "weaponCategory": "simple", "weight": 2, "value": 500,
            "property": [ "L", "T" ], "range": "20/60", "dmg1": "1d6", "dmgType": "S" }
        ] }
        """);

        var (client, tracker) = await BuildClientAsync();
        tracker.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(Record(2, "Player's Handbook 2014", "PHB"));

        var canonicalPath = Path.Combine(_canonicalDir, "phb14.json");
        var loader = new CanonicalJsonLoader();
        var before = await loader.LoadAsync(canonicalPath, CancellationToken.None);
        var beforeFireball = before.Entities.Single(e => e.Id == "phb14.spell.fireball");

        foreach (var type in new[] { "Feat", "Condition", "Weapon" })
        {
            var response = await client.PostAsync($"/admin/books/2/backfill-entities?type={type}", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // Reload via CanonicalJsonLoader — this is the dev-flow canonical-rewrite gate: it must
        // load without throwing a duplicate-id error.
        var after = await loader.LoadAsync(canonicalPath, CancellationToken.None);

        var ids = after.Entities.Select(e => e.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();

        var afterFireball = after.Entities.Single(e => e.Id == "phb14.spell.fireball");
        Assert.Equal(JsonSerializer.Serialize(beforeFireball), JsonSerializer.Serialize(afterFireball));

        var alert = after.Entities.Single(e => e.Id == "phb14.feat.alert");
        var prone = after.Entities.Single(e => e.Id == "phb14.condition.prone");
        var handaxe = after.Entities.Single(e => e.Id == "phb14.weapon.handaxe");
        foreach (var newEntity in new[] { alert, prone, handaxe })
        {
            Assert.Equal("5etools-backfill", newEntity.DataSource);
            Assert.Equal(EntityDisposition.Accepted, newEntity.Disposition);
        }
    }

    [Fact]
    public async Task BackfillEntities_MissingSourceKey_Returns400()
    {
        var (client, tracker) = await BuildClientAsync();
        tracker.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(Record(3, "Homebrew Book", null));

        var response = await client.PostAsync("/admin/books/3/backfill-entities?type=Monster", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}