using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Programmatic;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Programmatic;

public class OverridesStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ovr-" + Guid.NewGuid().ToString("N"));

    private OverridesStore Store() =>
        new(Options.Create(new EntityIngestionOptions { CanonicalDirectory = _dir }));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task Store_missing_file_is_empty()
    {
        var o = await Store().LoadAsync("nobook", CancellationToken.None);
        o.Overrides.Should().BeEmpty();
        o.Approvals.Should().BeEmpty();
    }

    [Fact]
    public async Task Store_roundtrip()
    {
        var at = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var saved = new EntityOverrides(
            [new FieldOverride("a.b", "fields.cr", JsonDocument.Parse("\"3\"").RootElement.Clone(),
                JsonDocument.Parse("\"2\"").RootElement.Clone(), "why", at)],
            [new Approval("a.c", "fine", at)]);
        var store = Store();
        await store.SaveAsync("book", saved, CancellationToken.None);

        var text = await File.ReadAllTextAsync(Path.Combine(_dir, "book.overrides.json"));
        text.Should().Contain("\"entityId\"").And.Contain("\n");
        File.Exists(Path.Combine(_dir, "book.overrides.json.tmp")).Should().BeFalse();

        var loaded = await store.LoadAsync("book", CancellationToken.None);
        loaded.Overrides.Should().ContainSingle();
        loaded.Overrides[0].EntityId.Should().Be("a.b");
        loaded.Overrides[0].Value.GetString().Should().Be("3");
        loaded.Overrides[0].Extracted!.Value.GetString().Should().Be("2");
        loaded.Overrides[0].At.Should().Be(at);
        loaded.Approvals.Should().ContainSingle().Which.Reason.Should().Be("fine");
    }

    [Fact]
    public async Task Store_roundtrips_declines()
    {
        var at = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var store = Store();
        await store.SaveAsync("book", new EntityOverrides([], [], [new Decline("a.lore.credit", "art credit", at)]), CancellationToken.None);

        (await File.ReadAllTextAsync(Path.Combine(_dir, "book.overrides.json"))).Should().Contain("\"declines\"");
        var loaded = await store.LoadAsync("book", CancellationToken.None);
        loaded.Declines.Should().ContainSingle().Which.Should().Be(new Decline("a.lore.credit", "art credit", at));
    }

    [Fact]
    public async Task Store_file_without_declines_key_loads_empty()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Path.Combine(_dir, "old.overrides.json"), """{"overrides":[],"approvals":[]}""");
        var loaded = await Store().LoadAsync("old", CancellationToken.None);
        loaded.Declines.Should().NotBeNull().And.BeEmpty();
    }
}
