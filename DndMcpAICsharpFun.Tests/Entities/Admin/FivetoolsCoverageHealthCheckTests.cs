using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

/// <summary>
/// Task 6, surface (c): startup coverage guard — one non-fatal Warning per official book below
/// 100% coverage, silent at 100%. Mirrors <c>ScopeHealthCheckTests</c>'s split between the static,
/// directly-testable core logic (<see cref="FivetoolsCoverageHealthCheck.WarnIfBelowFull"/>) and an
/// end-to-end <see cref="FivetoolsCoverageHealthCheck.StartAsync"/> pass with a real
/// <see cref="FivetoolsCoverageService"/> (built from a fake provider, like
/// <see cref="FivetoolsCoverageServiceTests"/>) since the service is a sealed class and cannot be
/// substituted.
/// </summary>
public sealed class FivetoolsCoverageHealthCheckTests : IDisposable
{
    private readonly string _root;
    private readonly string _fivetoolsDir;
    private readonly string _canonicalDir;

    public FivetoolsCoverageHealthCheckTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fivetools-coverage-health-" + Guid.NewGuid().ToString("N"));
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
          { "name": "Bugbear", "source": "MM", "page": 33, "size": ["M"], "type": "humanoid", "str": 15, "cr": "1" }
        ] }
        """);
    }

    private void WriteCanonical(params string[] presentMonsterNames)
    {
        var entities = string.Join(",\n", presentMonsterNames.Select(n => $$"""
            { "id": "mm14.monster.{{n.ToLowerInvariant()}}", "type": "Monster", "name": "{{n}}", "sourceBook": "MM",
              "edition": "Edition2014", "page": 1,
              "firstAppearedIn": { "book": "MM", "edition": "Edition2014", "page": 1 },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": { "str": 8 }, "dataSource": "", "srd": false, "srd52": false,
              "basicRules2024": false, "needsReview": false, "disposition": "Accepted", "keywords": [] }
            """));
        File.WriteAllText(Path.Combine(_canonicalDir, "mm14.json"), $$"""
        {
          "schemaVersion": "1",
          "book": { "sourceBook": "MM", "edition": "Edition2014", "fileHash": "", "displayName": "Monster Manual 2014" },
          "entities": [ {{entities}} ]
        }
        """);
    }

    private FivetoolsCoverageService BuildCoverageService()
    {
        var registry = new BookSourceRegistry(Path.Combine(_fivetoolsDir, "books.json"));
        var loader = new CanonicalJsonLoader();
        IReadOnlyDictionary<EntityType, EntityBackfillService> services = new Dictionary<EntityType, EntityBackfillService>
        {
            [EntityType.Monster] = new EntityBackfillService(
                new DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers.MonsterBackfillProvider(),
                registry, loader, _canonicalDir, _fivetoolsDir),
        };
        return new FivetoolsCoverageService(services, _fivetoolsDir);
    }

    private static IngestionRecord Record(int id, string? sourceKey) => new()
    {
        Id = id,
        FilePath = "/tmp/x.pdf",
        FileName = "x.pdf",
        FileHash = "h",
        Version = "5e",
        DisplayName = "Monster Manual 2014",
        Status = IngestionStatus.JsonIngested,
        FivetoolsSourceKey = sourceKey,
    };

    // ── Static core logic ────────────────────────────────────────────────────

    [Fact]
    public void WarnIfBelowFull_LogsWarning_NamingKeyPctAndMissingCount()
    {
        var coverage = new BookCoverage(
            "MM",
            [new TypeCoverage(EntityType.Monster, 2, 1, 1, ["Bugbear"])],
            [], TotalPresent: 1, TotalRoster: 2, CoveragePct: 50.0);
        var capturingLogger = new CapturingLogger<FivetoolsCoverageHealthCheck>();

        FivetoolsCoverageHealthCheck.WarnIfBelowFull(coverage, capturingLogger);

        capturingLogger.Logs.Should().ContainSingle(
            l => l.Level == LogLevel.Warning && l.Message.Contains("MM") && l.Message.Contains("50"),
            "the warning must name the book key and the coverage percentage");
    }

    [Fact]
    public void WarnIfBelowFull_IsSilent_AtFullCoverage()
    {
        var coverage = new BookCoverage(
            "MM",
            [new TypeCoverage(EntityType.Monster, 2, 2, 0, [])],
            [], TotalPresent: 2, TotalRoster: 2, CoveragePct: 100.0);
        var capturingLogger = new CapturingLogger<FivetoolsCoverageHealthCheck>();

        FivetoolsCoverageHealthCheck.WarnIfBelowFull(coverage, capturingLogger);

        capturingLogger.Logs.Should().BeEmpty();
    }

    [Fact]
    public void WarnIfBelowFull_IsSilent_WhenTotalRosterIsZero()
    {
        var capturingLogger = new CapturingLogger<FivetoolsCoverageHealthCheck>();

        FivetoolsCoverageHealthCheck.WarnIfBelowFull(BookCoverage.Empty, capturingLogger);

        capturingLogger.Logs.Should().BeEmpty();
    }

    // ── End-to-end StartAsync ────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_WarnsForBelowFullCoverageBook_EndToEnd()
    {
        WriteCanonical("Goblin"); // Bugbear missing → 50%
        var coverageService = BuildCoverageService();
        var tracker = Substitute.For<IIngestionTracker>();
        tracker.GetAllAsync(Arg.Any<int>(), 0, Arg.Any<CancellationToken>())
            .Returns(new List<IngestionRecord> { Record(1, "MM") });

        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddSingleton(coverageService);
        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var capturingLogger = new CapturingLogger<FivetoolsCoverageHealthCheck>();
        var check = new FivetoolsCoverageHealthCheck(scopeFactory, capturingLogger);

        await check.StartAsync(CancellationToken.None);

        capturingLogger.Logs.Should().ContainSingle(
            l => l.Level == LogLevel.Warning && l.Message.Contains("MM"));
    }

    [Fact]
    public async Task StartAsync_IsSilent_WhenBookIsAtFullCoverage()
    {
        WriteCanonical("Goblin", "Bugbear"); // both present → 100%
        var coverageService = BuildCoverageService();
        var tracker = Substitute.For<IIngestionTracker>();
        tracker.GetAllAsync(Arg.Any<int>(), 0, Arg.Any<CancellationToken>())
            .Returns(new List<IngestionRecord> { Record(1, "MM") });

        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddSingleton(coverageService);
        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var capturingLogger = new CapturingLogger<FivetoolsCoverageHealthCheck>();
        var check = new FivetoolsCoverageHealthCheck(scopeFactory, capturingLogger);

        await check.StartAsync(CancellationToken.None);

        capturingLogger.Logs.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_SkipsNonOfficialBooks()
    {
        WriteCanonical("Goblin");
        var coverageService = BuildCoverageService();
        var tracker = Substitute.For<IIngestionTracker>();
        tracker.GetAllAsync(Arg.Any<int>(), 0, Arg.Any<CancellationToken>())
            .Returns(new List<IngestionRecord> { Record(1, null) });

        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddSingleton(coverageService);
        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var capturingLogger = new CapturingLogger<FivetoolsCoverageHealthCheck>();
        var check = new FivetoolsCoverageHealthCheck(scopeFactory, capturingLogger);

        await check.StartAsync(CancellationToken.None);

        capturingLogger.Logs.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_DoesNotThrow_WhenTheTrackerFails()
    {
        var tracker = Substitute.For<IIngestionTracker>();
        tracker.GetAllAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task<List<IngestionRecord>>>(_ => throw new InvalidOperationException("DB unreachable"));

        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddSingleton(BuildCoverageService());
        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var capturingLogger = new CapturingLogger<FivetoolsCoverageHealthCheck>();
        var check = new FivetoolsCoverageHealthCheck(scopeFactory, capturingLogger);

        await FluentActions.Awaiting(() => check.StartAsync(CancellationToken.None))
            .Should().NotThrowAsync();
        capturingLogger.Logs.Should().Contain(l => l.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task StopAsync_CompletesImmediately()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IIngestionTracker>());
        services.AddSingleton(BuildCoverageService());
        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var check = new FivetoolsCoverageHealthCheck(scopeFactory, new CapturingLogger<FivetoolsCoverageHealthCheck>());

        var task = check.StopAsync(CancellationToken.None);

        task.IsCompletedSuccessfully.Should().BeTrue();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}