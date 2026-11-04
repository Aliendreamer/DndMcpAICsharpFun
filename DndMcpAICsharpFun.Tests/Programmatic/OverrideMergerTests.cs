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

    [Theory]
    [InlineData("fields.")]
    [InlineData("fields..cr")]
    [InlineData("fields.cr.")]
    [InlineData("fields.ac..ac")]
    public void Empty_path_segments_rejected(string path)
    {
        OverrideMerger.ValidatePath(Grell(), path).Should().BeFalse();
    }

    [Fact]
    public void Empty_path_segment_in_stored_override_is_unapplied_not_written()
    {
        var o = new EntityOverrides([Ov("fields.", "1")], []);
        var e = OverrideMerger.Apply([Grell()], o).Entities[0];
        e.Fields.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("cr", "ac");
        e.Checks.Should().Contain(c => c.Name == OverrideMerger.UnappliedCheck && !c.Passed);
    }

    private static EntityEnvelope WithFields(string json, IReadOnlyList<EntityCheck>? checks = null) =>
        Grell(checks: checks) with { Fields = J(json) };

    [Fact]
    public void Unapplied_override_sends_entity_to_review()
    {
        // "ac" parent removed by re-extraction
        var e = WithFields("""{"cr":"2"}""");
        var o = new EntityOverrides([Ov("fields.ac.0.ac", "15")], []);
        var r = OverrideMerger.Apply([e], o).Entities[0];
        r.Checks.Should().ContainSingle(c => c.Name == "override-unapplied" && !c.Passed
            && c.Reason.Contains("fields.ac.0.ac"));
        r.Disposition.Should().Be(EntityDisposition.NeedsReview);
        r.NeedsReview.Should().BeTrue();

        var approved = OverrideMerger.Apply([e], new EntityOverrides(o.Overrides, [new Approval(e.Id, "ok", At)]));
        approved.Entities[0].Disposition.Should().Be(EntityDisposition.NeedsReview);
    }

    [Fact]
    public void Wrong_value_kind_is_unapplied()
    {
        var o = new EntityOverrides([Ov("name", "5")], []);
        var r = OverrideMerger.Apply([Grell()], o).Entities[0];
        r.Name.Should().Be("Grell");
        r.Checks.Should().ContainSingle(c => c.Name == "override-unapplied");
        r.Disposition.Should().Be(EntityDisposition.NeedsReview);
    }

    [Fact]
    public void Object_key_order_does_not_drift()
    {
        var e = WithFields("""{"speed":{"walk":30,"fly":60},"list":[1,2]}""");
        var o = new EntityOverrides([Ov("fields.speed", """{"walk":30,"fly":90}""", """{"fly":60,"walk":30}""")], []);
        var r = OverrideMerger.Apply([e], o).Entities[0];
        r.Checks.Should().BeEmpty();
        r.Fields.GetProperty("speed").GetProperty("fly").GetInt32().Should().Be(90);
    }

    [Fact]
    public void Array_order_and_changed_object_value_drift()
    {
        var e = WithFields("""{"speed":{"walk":30,"fly":60},"list":[2,1]}""");
        var o = new EntityOverrides(
            [Ov("fields.list", "[3]", "[1,2]"), Ov("fields.speed", "{}", """{"walk":30,"fly":40}""")], []);
        var r = OverrideMerger.Apply([e], o).Entities[0];
        r.Checks.Count(c => c.Name == "override-drift").Should().Be(2);
        r.Checks.Should().Contain(c => c.Reason.Contains("at fields.list:"));
    }

    [Fact]
    public void Numeric_forms_compare_by_value()
    {
        // JsonNode.DeepEquals compares numbers by value: 2 and 2.0 are equal (no drift).
        var e = WithFields("""{"cr":2.0}""");
        var o = new EntityOverrides([Ov("fields.cr", "3", "2")], []);
        OverrideMerger.Apply([e], o).Entities[0].Checks.Should().BeEmpty();
        var e2 = WithFields("""{"cr":2.5}""");
        OverrideMerger.Apply([e2], o).Entities[0].Checks.Should().ContainSingle(c => c.Name == "override-drift");
    }

    [Fact]
    public void Drift_reason_names_field()
    {
        var o = new EntityOverrides([Ov("fields.cr", "\"3\"", "\"2\"")], []);
        var r = OverrideMerger.Apply([Grell("\"4\"")], o).Entities[0];
        r.Checks.Single().Reason.Should().StartWith("extraction changed under your override at fields.cr: was");
    }

    [Fact]
    public void ValidatePath_allows_new_leaf_and_null_value()
    {
        var e = WithFields("""{"speed":{"walk":30},"legendary":null}""");
        OverrideMerger.ValidatePath(e, "fields.speed.fly").Should().BeTrue();
        OverrideMerger.ValidatePath(e, "fields.legendary").Should().BeTrue();
        OverrideMerger.ValidatePath(e, "fields.nope.x").Should().BeFalse();
        OverrideMerger.ValidatePath(e, "fields.speed.walk.x").Should().BeFalse();
    }

    [Fact]
    public void Two_overrides_same_field_last_wins_drift_vs_original()
    {
        var o = new EntityOverrides(
            [Ov("fields.cr", "\"3\"", "\"2\""), Ov("fields.cr", "\"5\"", "\"2\"")], []);
        var r = OverrideMerger.Apply([Grell("\"2\"")], o).Entities[0];
        r.Fields.GetProperty("cr").GetString().Should().Be("5");
        r.Checks.Should().BeEmpty();
        r.Disposition.Should().Be(EntityDisposition.Accepted);
    }
}
