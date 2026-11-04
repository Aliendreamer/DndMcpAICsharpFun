using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Programmatic;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Programmatic;

public class OverrideMergerTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static EntityEnvelope Grell(string cr = "\"2\"", IReadOnlyList<EntityCheck>? checks = null) => new(
        "mm14.monster.grell", EntityType.Monster, "Grell", "MM", "2014", 1,
        new FirstAppearance("MM", "2014", 1), [], [], "Grell text",
        J($$"""{"cr":{{cr}},"ac":[{"ac":12}]}"""),
        Checks: checks!);

    private static FieldOverride Ov(string field, string value, string? extracted = null) =>
        new("mm14.monster.grell", field, J(value), extracted is null ? null : J(extracted), "fix", At);

    [Fact]
    public void Override_applied()
    {
        var o = new EntityOverrides([Ov("fields.cr", "\"3\"")], []);
        var r = OverrideMerger.Apply([Grell()], o);
        r.Entities[0].Fields.GetProperty("cr").GetString().Should().Be("3");
    }

    [Fact]
    public void Override_applied_to_array_index_and_roots()
    {
        var o = new EntityOverrides([Ov("fields.ac.0.ac", "15"), Ov("name", "\"Grell X\"")], []);
        var e = OverrideMerger.Apply([Grell()], o).Entities[0];
        e.Fields.GetProperty("ac")[0].GetProperty("ac").GetInt32().Should().Be(15);
        e.Name.Should().Be("Grell X");
    }

    [Fact]
    public void Override_survives_rerun()
    {
        var o = new EntityOverrides([Ov("fields.cr", "\"3\"", "\"2\"")], []);
        var first = OverrideMerger.Apply([Grell()], o).Entities[0];
        var second = OverrideMerger.Apply([Grell()], o).Entities[0];
        second.Fields.GetProperty("cr").GetString().Should().Be("3");
        second.Checks.Should().BeEquivalentTo(first.Checks);
        second.Disposition.Should().Be(EntityDisposition.Accepted);
    }

    [Fact]
    public void Drift_flags_entity()
    {
        var o = new EntityOverrides([Ov("fields.cr", "\"3\"", "\"2\"")], [new Approval("mm14.monster.grell", "ok", At)]);
        var e = OverrideMerger.Apply([Grell("\"4\"")], o).Entities[0];
        e.Fields.GetProperty("cr").GetString().Should().Be("3");
        e.Checks.Should().ContainSingle(c => c.Name == "override-drift" && !c.Passed
            && c.Reason.Contains("was \"2\"") && c.Reason.Contains("now \"4\""));
        e.Disposition.Should().Be(EntityDisposition.NeedsReview);
        e.NeedsReview.Should().BeTrue();
    }

    [Fact]
    public void Orphan_reported_and_kept()
    {
        var o = new EntityOverrides(
            [new FieldOverride("mm14.monster.gone", "fields.cr", J("\"1\""), null, "x", At)],
            [new Approval("mm14.monster.gone2", "x", At)]);
        var r = OverrideMerger.Apply([Grell()], o);
        r.OrphanedEntityIds.Should().BeEquivalentTo(["mm14.monster.gone", "mm14.monster.gone2"]);
        r.Entities.Should().ContainSingle().Which.Fields.GetProperty("cr").GetString().Should().Be("2");
        o.Overrides.Should().HaveCount(1);
    }

    [Fact]
    public void Approval_accepts_failed_entity()
    {
        var e = Grell(checks: [new EntityCheck("hp-math", false, "bad")]);
        OverrideMerger.SetDisposition(e, approved: false).Disposition.Should().Be(EntityDisposition.NeedsReview);
        var accepted = OverrideMerger.SetDisposition(e, approved: true);
        accepted.Disposition.Should().Be(EntityDisposition.Accepted);
        accepted.NeedsReview.Should().BeFalse();

        var viaApply = OverrideMerger.Apply([e], new EntityOverrides([], [new Approval(e.Id, "ok", At)]));
        viaApply.Entities[0].Disposition.Should().Be(EntityDisposition.Accepted);
    }

    [Fact]
    public void Clean_entity_accepted()
    {
        var e = Grell(checks: [new EntityCheck("hp-math", true, "")]) with { NeedsReview = true };
        var r = OverrideMerger.SetDisposition(e, approved: false);
        r.Disposition.Should().Be(EntityDisposition.Accepted);
        r.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public void Invalid_path_rejected()
    {
        var e = Grell();
        OverrideMerger.ValidatePath(e, "fields.nope.x").Should().BeFalse();
        OverrideMerger.ValidatePath(e, "bogus").Should().BeFalse();
        OverrideMerger.ValidatePath(e, "fields.cr").Should().BeTrue();
        OverrideMerger.ValidatePath(e, "fields.ac.0.ac").Should().BeTrue();
        OverrideMerger.ValidatePath(e, "fields.ac.5.ac").Should().BeFalse();
        OverrideMerger.ValidatePath(e, "name").Should().BeTrue();
        OverrideMerger.ValidatePath(e, "canonicalText").Should().BeTrue();
    }
}
