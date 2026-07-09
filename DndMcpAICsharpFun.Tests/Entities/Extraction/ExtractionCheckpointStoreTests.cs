using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public sealed class ExtractionCheckpointStoreTests
{
    private static readonly JsonSerializerOptions ReadOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    // ── LoadCheckpointAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task LoadCheckpointAsync_returns_empty_when_no_files_exist()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var store = new ExtractionCheckpointStore();
            var (extracted, errors, doneIds) =
                await store.LoadCheckpointAsync(
                    Path.Combine(dir, "progress.json"),
                    Path.Combine(dir, "progress.errors.json"));

            extracted.Should().BeEmpty();
            errors.Should().BeEmpty();
            doneIds.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task WriteCheckpointAsync_then_LoadCheckpointAsync_round_trips()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var progressPath = Path.Combine(dir, "progress.json");
        var errorsPath = Path.Combine(dir, "progress.errors.json");
        try
        {
            var store = new ExtractionCheckpointStore();

            var envelope = new EntityEnvelope(
                Id: "phb.monster.goblin",
                Type: EntityType.Monster,
                Name: "Goblin",
                SourceBook: "PHB",
                Edition: "5e",
                Page: 123,
                FirstAppearedIn: new FirstAppearance("PHB", "5e", 123),
                RevisedIn: Array.Empty<Revision>(),
                SettingTags: Array.Empty<string>(),
                CanonicalText: string.Empty,
                Fields: JsonDocument.Parse("{}").RootElement.Clone(),
                NeedsReview: false);

            var error = new ExtractionErrorEntry(
                SourceEntityId: "phb.spell.fireball",
                FieldPath: "(extraction)",
                MissingTargetId: string.Empty,
                ErrorKind: "extraction_failure",
                Detail: "LLM timed out");

            // Write
            await store.WriteCheckpointAsync(
                progressPath, errorsPath,
                new List<EntityEnvelope> { envelope },
                new List<ExtractionErrorEntry> { error });

            // Load back
            var (extracted, errors, doneIds) =
                await store.LoadCheckpointAsync(progressPath, errorsPath);

            extracted.Should().HaveCount(1);
            extracted[0].Id.Should().Be("phb.monster.goblin");
            extracted[0].Name.Should().Be("Goblin");

            errors.Should().HaveCount(1);
            errors[0].SourceEntityId.Should().Be("phb.spell.fireball");
            errors[0].ErrorKind.Should().Be("extraction_failure");

            doneIds.Should().BeEquivalentTo(["phb.monster.goblin", "phb.spell.fireball"]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task WriteCheckpointAsync_uses_atomic_tmp_move()
    {
        // Verify that no .tmp files are left after a successful write.
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var progressPath = Path.Combine(dir, "progress.json");
        var errorsPath = Path.Combine(dir, "progress.errors.json");
        try
        {
            var store = new ExtractionCheckpointStore();
            await store.WriteCheckpointAsync(progressPath, errorsPath, [], []);

            File.Exists(progressPath + ".tmp").Should().BeFalse("tmp file must be cleaned up");
            File.Exists(errorsPath + ".tmp").Should().BeFalse("tmp file must be cleaned up");
            File.Exists(progressPath).Should().BeTrue();
            File.Exists(errorsPath).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── DoneIds set semantics ─────────────────────────────────────────────────

    [Fact]
    public async Task LoadCheckpointAsync_doneIds_includes_both_extracted_and_error_ids()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var progressPath = Path.Combine(dir, "progress.json");
        var errorsPath = Path.Combine(dir, "progress.errors.json");
        try
        {
            var store = new ExtractionCheckpointStore();

            // Simulate progress.json with one entity + errors file with one error.
            var envelope = new EntityEnvelope(
                Id: "book.monster.orc", Type: EntityType.Monster, Name: "Orc",
                SourceBook: "B", Edition: "5e", Page: 1,
                FirstAppearedIn: new FirstAppearance("B", "5e", 1),
                RevisedIn: Array.Empty<Revision>(), SettingTags: Array.Empty<string>(),
                CanonicalText: string.Empty,
                Fields: JsonDocument.Parse("{}").RootElement.Clone(),
                NeedsReview: false);

            var error = new ExtractionErrorEntry("book.spell.sleep", "(type)", string.Empty, "no_schema", null);

            await store.WriteCheckpointAsync(progressPath, errorsPath,
                [envelope], [error]);

            var (_, _, doneIds) = await store.LoadCheckpointAsync(progressPath, errorsPath);

            doneIds.Should().Contain("book.monster.orc");
            doneIds.Should().Contain("book.spell.sleep");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}