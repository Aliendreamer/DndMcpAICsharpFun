using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

public class CanonicalValidationEndpointTests
{
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
                NullLogger<CanonicalValidationService>.Instance);

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
                NullLogger<CanonicalValidationService>.Instance);

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
                NullLogger<CanonicalValidationService>.Instance);

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
                NullLogger<CanonicalValidationService>.Instance);

            var report = await svc.ValidateAsync(CancellationToken.None);
            report.NeedsReview.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}