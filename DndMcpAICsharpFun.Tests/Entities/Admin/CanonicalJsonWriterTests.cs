using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

public sealed class CanonicalJsonWriterTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _dir;
    private readonly CanonicalJsonLoader _loader = new();
    private readonly CanonicalJsonWriter _writer = new();

    public CanonicalJsonWriterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"CanonicalJsonWriterTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CopyFixture(string fixtureName)
    {
        var src  = Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical", fixtureName);
        var dest = Path.Combine(_dir, fixtureName);
        File.Copy(src, dest, overwrite: true);
        return dest;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PatchEntity_ClearFlag_FlagIsClearedAndOtherEntitiesUnchanged()
    {
        // ARRANGE — copy fixture to temp dir so we can mutate it.
        var path = CopyFixture("needs-review-book.json");

        // Confirm the entity starts as NeedsReview = true.
        var before = await _loader.LoadAsync(path, CancellationToken.None);
        before.Entities.Single(e => e.Id == "needs-review-book.spell.fireball")
            .NeedsReview.Should().BeTrue();

        // ACT — accept (clear flag, no name/fields changes).
        var result = await _writer.PatchEntityAsync(
            path, "needs-review-book.spell.fireball", null, null, _loader, CancellationToken.None);

        // ASSERT — returned true.
        result.Should().BeTrue();

        // Reload from disk and verify.
        var after = await _loader.LoadAsync(path, CancellationToken.None);
        var fireball = after.Entities.Single(e => e.Id == "needs-review-book.spell.fireball");
        fireball.NeedsReview.Should().BeFalse("flag should be cleared");
        fireball.Name.Should().Be("Fireball", "name should be unchanged");

        // Other entities must be unchanged.
        var bullywug = after.Entities.Single(e => e.Id == "needs-review-book.monster.b ullywug");
        bullywug.NeedsReview.Should().BeTrue("other entity should remain untouched");

        var fighter = after.Entities.Single(e => e.Id == "needs-review-book.class.fighter");
        fighter.NeedsReview.Should().BeFalse("already-clear entity still false");
        fighter.Name.Should().Be("Fighter");
    }

    [Fact]
    public async Task PatchEntity_EditNameAndFields_NameAndFieldsUpdatedFlagCleared()
    {
        var path = CopyFixture("needs-review-book.json");

        // Build a fields patch.
        var fieldsJson  = """{"level": 99, "newKey": "hello"}""";
        var patchFields = JsonDocument.Parse(fieldsJson).RootElement;

        // ACT.
        var result = await _writer.PatchEntityAsync(
            path,
            "needs-review-book.spell.fireball",
            "Fireball (Corrected)",
            patchFields,
            _loader,
            CancellationToken.None);

        // ASSERT.
        result.Should().BeTrue();

        var after    = await _loader.LoadAsync(path, CancellationToken.None);
        var fireball = after.Entities.Single(e => e.Id == "needs-review-book.spell.fireball");

        fireball.NeedsReview.Should().BeFalse();
        fireball.Name.Should().Be("Fireball (Corrected)");

        // Patched fields: overwritten keys win, existing keys remain.
        fireball.Fields.GetProperty("level").GetInt32().Should().Be(99);
        fireball.Fields.GetProperty("newKey").GetString().Should().Be("hello");
        fireball.Fields.GetProperty("school").GetString().Should().Be("V", "untouched key preserved");

        // Other entities completely unchanged.
        after.Entities.Single(e => e.Id == "needs-review-book.class.fighter")
            .Name.Should().Be("Fighter");
    }

    [Fact]
    public async Task PatchEntity_UnknownId_ReturnsFalse()
    {
        var path = CopyFixture("needs-review-book.json");

        var result = await _writer.PatchEntityAsync(
            path, "nonexistent.entity.id", null, null, _loader, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task PatchEntity_IdempotentAccept_FileStillValid()
    {
        var path = CopyFixture("needs-review-book.json");

        // Accept once.
        await _writer.PatchEntityAsync(path, "needs-review-book.spell.fireball", null, null, _loader, CancellationToken.None);

        // Accept again — should succeed without throwing.
        var secondResult = await _writer.PatchEntityAsync(
            path, "needs-review-book.spell.fireball", null, null, _loader, CancellationToken.None);

        secondResult.Should().BeTrue();
        var after = await _loader.LoadAsync(path, CancellationToken.None);
        after.Entities.Single(e => e.Id == "needs-review-book.spell.fireball")
            .NeedsReview.Should().BeFalse();
    }

    [Fact]
    public async Task PatchEntity_EntityCountPreserved_AfterWrite()
    {
        var path = CopyFixture("needs-review-book.json");
        var before = await _loader.LoadAsync(path, CancellationToken.None);
        int countBefore = before.Entities.Count;

        await _writer.PatchEntityAsync(path, "needs-review-book.spell.fireball", null, null, _loader, CancellationToken.None);

        var after = await _loader.LoadAsync(path, CancellationToken.None);
        after.Entities.Should().HaveCount(countBefore, "write must not drop or duplicate entities");
    }
}
