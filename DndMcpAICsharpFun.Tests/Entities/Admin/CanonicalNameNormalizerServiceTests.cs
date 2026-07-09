using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

public class CanonicalNameNormalizerServiceTests
{
    // ── DndTitleCase unit tests ──────────────────────────────────────────────

    [Theory]
    [InlineData("FIREBALL", "Fireball")]
    [InlineData("CIRCLE OF SPORES", "Circle of Spores")]
    [InlineData("OF MICE AND MEN", "Of Mice and Men")]
    [InlineData("TASHA'S CAULDRON", "Tasha's Cauldron")]
    [InlineData("SPIDER-CLIMB", "Spider-Climb")]
    public void DndTitleCase_converts_all_caps_correctly(string input, string expected)
        => CanonicalNameNormalizerService.DndTitleCase(input).Should().Be(expected);

    // ── NormalizeAsync integration tests ────────────────────────────────────

    private static async Task<CanonicalNameNormalizerReport> RunNormalizer(
        string json, bool dryRun = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "test.json"), json);
            var svc = new CanonicalNameNormalizerService(
                new CanonicalJsonLoader(),
                Options.Create(new EntityExtractionOptions { CanonicalDirectory = dir }));
            return await svc.NormalizeAsync(dryRun, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string OneEntityJson(string name, bool needsReview = false) => $$"""
        {
          "schemaVersion": "1",
          "book": { "sourceBook": "TEST", "edition": "Edition2014", "fileHash": "", "displayName": "Test" },
          "entities": [{
            "id": "test.class.x",
            "type": "Class",
            "name": "{{name}}",
            "sourceBook": "TEST",
            "edition": "Edition2014",
            "needsReview": {{(needsReview ? "true" : "false")}},
            "dataSource": "",
            "srd": false, "srd52": false, "basicRules2024": false,
            "firstAppearedIn": { "book": "TEST", "edition": "Edition2014" },
            "revisedIn": [], "settingTags": [], "canonicalText": "", "fields": {}
          }]
        }
        """;

    [Fact]
    public async Task AllCaps_entity_gets_title_cased_and_needsReview_stays_false()
    {
        var report = await RunNormalizer(OneEntityJson("CIRCLE OF SPORES"));
        report.Changes.Should().HaveCount(1);
        report.Changes[0].TitleCased.Should().Be(1);
        report.Changes[0].Flagged.Should().Be(0);
    }

    [Fact]
    public async Task OcrGarbled_entity_gets_flagged_and_name_unchanged()
    {
        var report = await RunNormalizer(OneEntityJson("Path of the Beast f eature"));
        report.Changes[0].Flagged.Should().Be(1);
        report.Changes[0].TitleCased.Should().Be(0);
    }

    [Fact]
    public async Task AllCaps_with_needsReview_true_gets_title_cased_and_flag_cleared()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "test.json");
            await File.WriteAllTextAsync(filePath, OneEntityJson("CIRCLE OF SPORES", needsReview: true));

            var svc = new CanonicalNameNormalizerService(
                new CanonicalJsonLoader(),
                Options.Create(new EntityExtractionOptions { CanonicalDirectory = dir }));

            await svc.NormalizeAsync(dryRun: false, CancellationToken.None);

            var reloaded = await new CanonicalJsonLoader().LoadAsync(filePath, CancellationToken.None);
            var entity = reloaded.Entities.Single();
            entity.Name.Should().Be("Circle of Spores");
            entity.NeedsReview.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task OcrGarbled_entity_needsReview_is_set_true_in_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "test.json");
            await File.WriteAllTextAsync(filePath, OneEntityJson("Path of the Beast f eature", needsReview: false));

            var svc = new CanonicalNameNormalizerService(
                new CanonicalJsonLoader(),
                Options.Create(new EntityExtractionOptions { CanonicalDirectory = dir }));

            await svc.NormalizeAsync(dryRun: false, CancellationToken.None);

            var reloaded = await new CanonicalJsonLoader().LoadAsync(filePath, CancellationToken.None);
            var entity = reloaded.Entities.Single();
            entity.Name.Should().Be("Path of the Beast f eature");
            entity.NeedsReview.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Clean_entity_is_unchanged()
    {
        var report = await RunNormalizer(OneEntityJson("Fighter"));
        report.Changes[0].Unchanged.Should().Be(1);
        report.Changes[0].TitleCased.Should().Be(0);
        report.Changes[0].Flagged.Should().Be(0);
    }

    [Fact]
    public async Task DryRun_returns_counts_without_writing()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "test.json");
            var originalJson = OneEntityJson("FIREBALL");
            await File.WriteAllTextAsync(filePath, originalJson);

            var svc = new CanonicalNameNormalizerService(
                new CanonicalJsonLoader(),
                Options.Create(new EntityExtractionOptions { CanonicalDirectory = dir }));

            var report = await svc.NormalizeAsync(dryRun: true, CancellationToken.None);
            report.DryRun.Should().BeTrue();
            report.Changes[0].TitleCased.Should().Be(1);

            // File must not be modified
            var afterJson = await File.ReadAllTextAsync(filePath);
            afterJson.Should().Be(originalJson);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Service_is_idempotent()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "test.json");
            await File.WriteAllTextAsync(filePath, OneEntityJson("CIRCLE OF SPORES"));

            var svc = new CanonicalNameNormalizerService(
                new CanonicalJsonLoader(),
                Options.Create(new EntityExtractionOptions { CanonicalDirectory = dir }));

            await svc.NormalizeAsync(dryRun: false, CancellationToken.None);
            var after1 = await File.ReadAllTextAsync(filePath);

            await svc.NormalizeAsync(dryRun: false, CancellationToken.None);
            var after2 = await File.ReadAllTextAsync(filePath);

            after2.Should().Be(after1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}