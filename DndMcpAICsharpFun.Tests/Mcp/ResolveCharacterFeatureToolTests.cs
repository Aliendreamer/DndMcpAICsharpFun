using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Mcp;
using DndMcpAICsharpFun.Features.Resolution;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using DndMcpAICsharpFun.Infrastructure.Persistence;
using DndMcpAICsharpFun.Tests;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Mcp;

[Collection("postgres")]
public sealed class ResolveCharacterFeatureToolTests(PostgresFixture pg) : IAsyncLifetime
{
    private static readonly string DragonbornSlicePath =
        TestPaths.RepoFile("books/canonical/dragonborn-slice.json");

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ─── helpers ────────────────────────────────────────────────────────────

    private IDbContextFactory<AppDbContext> DbFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(opts =>
            opts.UseNpgsql(pg.Container.GetConnectionString()));
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    private DndMcpTools BuildTools()
    {
        var dbf     = DbFactory();
        var heroes  = new HeroRepository(dbf);
        var svc     = new CharacterResolutionService(dbf, heroes);

        // The other three constructor params (ragService, entityService, fusedService)
        // are not touched by resolve_character_feature — pass NSubstitute stubs.
        var ragService    = Substitute.For<IRagRetrievalService>();
        var entityService = Substitute.For<IEntityRetrievalService>();
        var fusedService  = Substitute.For<IFusedRetrievalService>();

        return new DndMcpTools(ragService, entityService, fusedService, svc);
    }

    private async Task<long> SeedSnapshotAsync(int level)
    {
        var sheet = new CharacterSheet
        {
            Race            = "Dragonborn",
            Level           = level,
            Constitution    = 16,
            ResolvedChoices = new Dictionary<string, string>
            {
                ["ancestry"] = "phb14.choiceset.draconic-ancestry:Red",
            },
        };

        await using var db = pg.NewContext();
        db.HeroSnapshots.Add(new HeroSnapshot(0, 1, level, $"L{level}", level, DateTime.UtcNow, sheet));
        await db.SaveChangesAsync();
        return await db.HeroSnapshots
            .Where(s => s.HeroId == 1 && s.SessionNumber == level)
            .Select(s => s.Id)
            .FirstAsync();
    }

    private async Task SeedStructuredFactsAsync()
    {
        var loader    = new CanonicalJsonLoader();
        var dbf       = DbFactory();
        var projector = new StructuredFactProjector(dbf);
        var file      = await loader.LoadAsync(DragonbornSlicePath, CancellationToken.None);
        await projector.ProjectAsync(file, CancellationToken.None);
    }

    // ─── tests ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Confirms the container actually started (not skipped/mocked).
    /// </summary>
    [Fact]
    public void Postgres_container_is_running()
    {
        pg.Container.State.Should().Be(DotNet.Testcontainers.Containers.TestcontainersStates.Running,
            "Testcontainers must have started the Postgres container");
    }

    /// <summary>
    /// resolve_character_feature returns a JSON string with fire, 15 ft. cone,
    /// Dexterity, DC 15, 3d6, and a non-null provenance blockId for a Red Dragonborn
    /// at Level 11 with Constitution 16.
    /// </summary>
    [Fact]
    public async Task ResolveCharacterFeature_RedDragonborn_Level11_returns_expected_json()
    {
        // Arrange
        await SeedStructuredFactsAsync();
        var snapshotId = await SeedSnapshotAsync(level: 11);
        var tools = BuildTools();

        // Act
        var json = await tools.resolve_character_feature(snapshotId, "breath weapon");

        // Assert — raw JSON contains expected substrings
        json.Should().Contain("fire",       "Red dragon deals fire damage");
        json.Should().Contain("15 ft. cone","Red dragon has cone breath");
        json.Should().Contain("Dexterity",  "Red dragon breath requires Dex save");
        json.Should().Contain("DC 15",      "DC 15 for Level 11 Con 16");
        json.Should().Contain("3d6",        "Tier 3 (L11) → 3d6");

        // Assert — the JSON deserialises cleanly and has provenance
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("confidence").GetString().Should().Be("ok");

        // At least one component must carry a non-null provenance with a blockId
        var components = root.GetProperty("components").EnumerateArray().ToList();
        components.Should().HaveCountGreaterThanOrEqualTo(4);

        var hasProvenance = components.Any(c =>
            c.TryGetProperty("provenance", out var prov) &&
            prov.ValueKind == JsonValueKind.Object &&
            prov.TryGetProperty("blockId", out var bid) &&
            bid.ValueKind != JsonValueKind.Null &&
            !string.IsNullOrEmpty(bid.GetString()));

        hasProvenance.Should().BeTrue("at least one component must carry a blockId from the PHB source");
    }
}
