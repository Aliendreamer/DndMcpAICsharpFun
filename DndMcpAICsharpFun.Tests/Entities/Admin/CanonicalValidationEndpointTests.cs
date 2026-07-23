using System.Net.Http.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

public class CanonicalValidationEndpointTests
{
    /// <summary>
    /// No providers registered — <see cref="FivetoolsCoverageService.ComputeAsync"/> then always
    /// yields <c>TotalRoster == 0</c> for any book, so it never contributes a coverage warning to
    /// these pre-existing validation tests (which are not about coverage).
    /// </summary>
    private static FivetoolsCoverageService NoCoverageService()
        => new(new Dictionary<EntityType, EntityBackfillService>(), Path.GetTempPath());

    [Fact]
    public async Task Empty_directory_returns_empty_report()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var svc = new CanonicalValidationService(
                new CanonicalJsonLoader(),
                new EntityReferenceResolver(),
                Options.Create(new EntityExtractionOptions { CanonicalDirectory = dir }),
                NullLogger<CanonicalValidationService>.Instance,
                NoCoverageService());

            var report = await svc.ValidateAsync(CancellationToken.None);
            report.FilesScanned.Should().Be(0);
            report.TotalEntities.Should().Be(0);
            report.Failures.Should().BeEmpty();
            report.Warnings.Should().BeEmpty();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Schema_version_mismatch_is_reported_as_failure()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dir, "broken.json"),
                """{"schemaVersion":"99","book":{"sourceBook":"x","edition":"e","fileHash":"h","displayName":"x"},"entities":[]}""");

            var svc = new CanonicalValidationService(
                new CanonicalJsonLoader(),
                new EntityReferenceResolver(),
                Options.Create(new EntityExtractionOptions { CanonicalDirectory = dir }),
                NullLogger<CanonicalValidationService>.Instance,
                NoCoverageService());

            var report = await svc.ValidateAsync(CancellationToken.None);
            report.Failures.Should().ContainSingle(f => f.Kind == "schema_validation_failure" && f.File == "broken.json");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Entities_with_needsReview_produce_warning()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var json = """
            {
              "schemaVersion": "1",
              "book": { "sourceBook": "TEST", "edition": "Edition2014", "fileHash": "aabbccdd", "displayName": "Test Book" },
              "entities": [
                {
                  "id": "test.class.alpha",
                  "type": "Class",
                  "name": "ALPHA",
                  "sourceBook": "TEST",
                  "edition": "Edition2014",
                  "needsReview": true,
                  "dataSource": "",
                  "srd": false, "srd52": false, "basicRules2024": false,
                  "firstAppearedIn": { "book": "TEST", "edition": "Edition2014" },
                  "revisedIn": [], "settingTags": [], "canonicalText": "",
                  "fields": {}
                },
                {
                  "id": "test.class.beta",
                  "type": "Class",
                  "name": "Beta",
                  "sourceBook": "TEST",
                  "edition": "Edition2014",
                  "needsReview": true,
                  "dataSource": "",
                  "srd": false, "srd52": false, "basicRules2024": false,
                  "firstAppearedIn": { "book": "TEST", "edition": "Edition2014" },
                  "revisedIn": [], "settingTags": [], "canonicalText": "",
                  "fields": {}
                }
              ]
            }
            """;
            await File.WriteAllTextAsync(Path.Combine(dir, "test.json"), json);

            var svc = new CanonicalValidationService(
                new CanonicalJsonLoader(),
                new EntityReferenceResolver(),
                Options.Create(new EntityExtractionOptions { CanonicalDirectory = dir }),
                NullLogger<CanonicalValidationService>.Instance,
                NoCoverageService());

            var report = await svc.ValidateAsync(CancellationToken.None);
            report.Failures.Should().BeEmpty();
            report.NeedsReview.Should().HaveCount(1);
            report.NeedsReview[0].File.Should().Be("test.json");
            report.NeedsReview[0].Count.Should().Be(2);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── Task 6, surface (b): coverage warning folded into validate — never a Failure/422 ──────────

    [Fact]
    public async Task Coverage_BelowFull_IsReportedAsWarning_NotAsFailure()
    {
        var canonicalDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var fivetoolsDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(canonicalDir);
        Directory.CreateDirectory(Path.Combine(fivetoolsDir, "bestiary"));
        try
        {
            File.WriteAllText(Path.Combine(fivetoolsDir, "books.json"), """
            { "book": [ { "id": "MM", "name": "Monster Manual (2014)", "group": "core", "published": "2014-09-30" } ] }
            """);
            File.WriteAllText(Path.Combine(fivetoolsDir, "bestiary", "bestiary-mm.json"), """
            { "monster": [
              { "name": "Goblin", "source": "MM", "page": 166, "size": ["S"], "type": "humanoid", "str": 8, "cr": "1/4" },
              { "name": "Bugbear", "source": "MM", "page": 33, "size": ["M"], "type": "humanoid", "str": 15, "cr": "1" }
            ] }
            """);
            await File.WriteAllTextAsync(Path.Combine(canonicalDir, "mm14.json"), """
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

            var registry = new BookSourceRegistry(Path.Combine(fivetoolsDir, "books.json"));
            var loader = new CanonicalJsonLoader();
            IReadOnlyDictionary<EntityType, EntityBackfillService> services = new Dictionary<EntityType, EntityBackfillService>
            {
                [EntityType.Monster] = new EntityBackfillService(
                    new DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers.MonsterBackfillProvider(),
                    registry, loader, canonicalDir, fivetoolsDir),
            };
            var coverageService = new FivetoolsCoverageService(services, fivetoolsDir);

            var svc = new CanonicalValidationService(
                loader,
                new EntityReferenceResolver(),
                Options.Create(new EntityExtractionOptions { CanonicalDirectory = canonicalDir }),
                NullLogger<CanonicalValidationService>.Instance,
                coverageService);

            var report = await svc.ValidateAsync(CancellationToken.None);

            report.Failures.Should().BeEmpty("coverage must never cause a validation failure");
            report.Coverage.Should().ContainSingle();
            var warning = report.Coverage[0];
            warning.File.Should().Be("mm14.json");
            warning.SourceKey.Should().Be("MM");
            warning.CoveragePct.Should().Be(50.0);
            warning.TotalMissing.Should().Be(1);
        }
        finally
        {
            Directory.Delete(canonicalDir, recursive: true);
            Directory.Delete(fivetoolsDir, recursive: true);
        }
    }

    [Fact]
    public async Task Coverage_BelowFull_ValidateEndpoint_StillReturns200()
    {
        var canonicalDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var fivetoolsDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(canonicalDir);
        Directory.CreateDirectory(Path.Combine(fivetoolsDir, "bestiary"));
        try
        {
            File.WriteAllText(Path.Combine(fivetoolsDir, "books.json"), """
            { "book": [ { "id": "MM", "name": "Monster Manual (2014)", "group": "core", "published": "2014-09-30" } ] }
            """);
            File.WriteAllText(Path.Combine(fivetoolsDir, "bestiary", "bestiary-mm.json"), """
            { "monster": [
              { "name": "Goblin", "source": "MM", "page": 166, "size": ["S"], "type": "humanoid", "str": 8, "cr": "1/4" },
              { "name": "Bugbear", "source": "MM", "page": 33, "size": ["M"], "type": "humanoid", "str": 15, "cr": "1" }
            ] }
            """);
            await File.WriteAllTextAsync(Path.Combine(canonicalDir, "mm14.json"), """
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

            var registry = new BookSourceRegistry(Path.Combine(fivetoolsDir, "books.json"));
            var loader = new CanonicalJsonLoader();
            IReadOnlyDictionary<EntityType, EntityBackfillService> services = new Dictionary<EntityType, EntityBackfillService>
            {
                [EntityType.Monster] = new EntityBackfillService(
                    new DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers.MonsterBackfillProvider(),
                    registry, loader, canonicalDir, fivetoolsDir),
            };
            var coverageService = new FivetoolsCoverageService(services, fivetoolsDir);
            var validationService = new CanonicalValidationService(
                loader,
                new EntityReferenceResolver(),
                Options.Create(new EntityExtractionOptions { CanonicalDirectory = canonicalDir }),
                NullLogger<CanonicalValidationService>.Instance,
                coverageService);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(validationService);
            var app = builder.Build();
            app.MapGroup("/admin").MapCanonicalValidation();
            await app.StartAsync();
            var client = app.GetTestClient();

            var response = await client.PostAsync("/admin/canonical/validate", content: null);

            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK,
                "coverage below 100% must warn, never turn a clean corpus into a 422");
            var body = await response.Content.ReadFromJsonAsync<CanonicalValidationReport>();
            body!.Coverage.Should().ContainSingle(w => w.SourceKey == "MM" && w.CoveragePct == 50.0);
        }
        finally
        {
            Directory.Delete(canonicalDir, recursive: true);
            Directory.Delete(fivetoolsDir, recursive: true);
        }
    }

    [Fact]
    public async Task No_needsReview_entities_produces_no_warning()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var json = """
            {
              "schemaVersion": "1",
              "book": { "sourceBook": "TEST", "edition": "Edition2014", "fileHash": "aabbccdd", "displayName": "Test Book" },
              "entities": [
                {
                  "id": "test.class.clean",
                  "type": "Class",
                  "name": "Clean",
                  "sourceBook": "TEST",
                  "edition": "Edition2014",
                  "needsReview": false,
                  "dataSource": "",
                  "srd": false, "srd52": false, "basicRules2024": false,
                  "firstAppearedIn": { "book": "TEST", "edition": "Edition2014" },
                  "revisedIn": [], "settingTags": [], "canonicalText": "",
                  "fields": {}
                }
              ]
            }
            """;
            await File.WriteAllTextAsync(Path.Combine(dir, "test.json"), json);

            var svc = new CanonicalValidationService(
                new CanonicalJsonLoader(),
                new EntityReferenceResolver(),
                Options.Create(new EntityExtractionOptions { CanonicalDirectory = dir }),
                NullLogger<CanonicalValidationService>.Instance,
                NoCoverageService());

            var report = await svc.ValidateAsync(CancellationToken.None);
            report.NeedsReview.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}