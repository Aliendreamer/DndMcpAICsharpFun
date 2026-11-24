using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Programmatic;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Programmatic;

public class OverrideMigrationTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static EntityEnvelope Ent(string id, EntityType type = EntityType.Rule, int? page = 10,
        int[]? provPages = null) => new(
        id, type, id, "PHB", "2014", page,
        new FirstAppearance("PHB", "2014", page), [], [], "text",
        JsonDocument.Parse("{}").RootElement.Clone(),
        Provenance: provPages is null
            ? null
            : new EntityProvenance(provPages.Select(p => new ProvenancePage(p, [])).ToList()));

    private static EntityOverrides Overrides(string id) => new(
        [new FieldOverride(id, "fields.x", JsonDocument.Parse("\"v\"").RootElement.Clone(),
            JsonDocument.Parse("\"e\"").RootElement.Clone(), "fix", At)],
        [new Approval(id, "ok", At, ["c"])]);

    [Fact]
    public void Caasting_time_moves_to_casting_time()
    {
        var r = OverrideMigration.Migrate(Overrides("phb14.rule.caasting-time"),
            [Ent("phb14.rule.caasting-time")], [Ent("phb14.rule.casting-time")]);

        r.Moved.Should().Equal(("phb14.rule.caasting-time", "phb14.rule.casting-time"));
        r.Orphaned.Should().BeEmpty();
        r.Overrides.Overrides.Should().ContainSingle().Which.EntityId.Should().Be("phb14.rule.casting-time");
        r.Overrides.Overrides[0].Reason.Should().Be("fix");
        r.Overrides.Overrides[0].At.Should().Be(At);
        r.Overrides.Overrides[0].Extracted.Should().NotBeNull();
        r.Overrides.Approvals.Should().ContainSingle().Which.EntityId.Should().Be("phb14.rule.casting-time");
    }

    [Fact]
    public void Records_of_the_hosts_rescue_move_with_the_host()
    {
        var o = Overrides("phb14.rule.caasting-time");
        var rescueOv = new FieldOverride("phb14.lore.caasting-time-trimmed", "canonicalText",
            JsonDocument.Parse("\"v\"").RootElement.Clone(), null, "edit", At);
        o = new EntityOverrides([.. o.Overrides, rescueOv], o.Approvals,
            [new Decline("phb14.lore.caasting-time-trimmed-2", "junk", At)]);
        var r = OverrideMigration.Migrate(o, [Ent("phb14.rule.caasting-time")], [Ent("phb14.rule.casting-time")]);

        r.Overrides.Overrides.Select(x => x.EntityId).Should().Contain("phb14.lore.casting-time-trimmed");
        r.Overrides.Overrides.Select(x => x.EntityId).Should().NotContain("phb14.lore.caasting-time-trimmed");
        r.Overrides.Declines.Should().ContainSingle().Which.EntityId.Should().Be("phb14.lore.casting-time-trimmed-2");
    }

    [Fact]
    public void Rescue_ids_are_never_moved_or_orphaned()
    {
        const string rescue = "mm14.lore.grell-trimmed";
        var o = new EntityOverrides(
            [new FieldOverride(rescue, "canonicalText", JsonDocument.Parse("\"v\"").RootElement.Clone(), null, "edit", At)],
            [], [new Decline(rescue, "junk", At)]);
        var r = OverrideMigration.Migrate(o,
            [Ent(rescue, EntityType.Lore)], [Ent("mm14.lore.grell-trimmer", EntityType.Lore)]);

        r.Moved.Should().BeEmpty();
        r.Orphaned.Should().BeEmpty();
        r.Overrides.Overrides[0].EntityId.Should().Be(rescue);
        r.Overrides.Declines[0].EntityId.Should().Be(rescue);
    }

    [Fact]
    public void Two_equally_close_candidates_stay_orphaned()
    {
        var r = OverrideMigration.Migrate(Overrides("phb14.rule.caasting-time"),
            [Ent("phb14.rule.caasting-time")],
            [Ent("phb14.rule.casting-time"), Ent("phb14.rule.castting-time")]);

        r.Moved.Should().BeEmpty();
        r.Orphaned.Should().Equal("phb14.rule.caasting-time");
        r.Overrides.Overrides[0].EntityId.Should().Be("phb14.rule.caasting-time");
    }

    [Fact]
    public void No_candidate_on_the_page_stays_orphaned()
    {
        var r = OverrideMigration.Migrate(Overrides("phb14.rule.caasting-time"),
            [Ent("phb14.rule.caasting-time", page: 10)], [Ent("phb14.rule.casting-time", page: 99)]);

        r.Moved.Should().BeEmpty();
        r.Orphaned.Should().Equal("phb14.rule.caasting-time");
    }

    [Fact]
    public void Candidate_matched_through_provenance_pages()
    {
        var r = OverrideMigration.Migrate(Overrides("phb14.rule.caasting-time"),
            [Ent("phb14.rule.caasting-time", page: 10)],
            [Ent("phb14.rule.casting-time", page: 9, provPages: [9, 10])]);

        r.Moved.Should().Equal(("phb14.rule.caasting-time", "phb14.rule.casting-time"));
    }

    [Fact]
    public void Different_type_is_never_a_candidate()
    {
        var r = OverrideMigration.Migrate(Overrides("phb14.rule.caasting-time"),
            [Ent("phb14.rule.caasting-time")], [Ent("phb14.spell.casting-time", EntityType.Spell)]);

        r.Moved.Should().BeEmpty();
        r.Orphaned.Should().Equal("phb14.rule.caasting-time");
    }

    [Fact]
    public void Present_ids_are_untouched()
    {
        var r = OverrideMigration.Migrate(Overrides("phb14.rule.casting-time"),
            [Ent("phb14.rule.casting-time")], [Ent("phb14.rule.casting-time"), Ent("phb14.rule.casting-times")]);

        r.Moved.Should().BeEmpty();
        r.Orphaned.Should().BeEmpty();
        r.Overrides.Overrides.Select(x => x.EntityId).Should().Equal("phb14.rule.casting-time");
        r.Overrides.Approvals.Select(x => x.EntityId).Should().Equal("phb14.rule.casting-time");
    }

    [Fact]
    public void Candidate_that_existed_before_is_not_a_rename_target()
    {
        var r = OverrideMigration.Migrate(Overrides("phb14.rule.caasting-time"),
            [Ent("phb14.rule.caasting-time"), Ent("phb14.rule.casting-time")],
            [Ent("phb14.rule.casting-time")]);

        r.Moved.Should().BeEmpty();
        r.Orphaned.Should().Equal("phb14.rule.caasting-time");
    }

    [Fact]
    public void Id_unknown_to_previous_stays_orphaned()
    {
        var r = OverrideMigration.Migrate(Overrides("phb14.rule.caasting-time"),
            [], [Ent("phb14.rule.casting-time")]);

        r.Moved.Should().BeEmpty();
        r.Orphaned.Should().Equal("phb14.rule.caasting-time");
    }

    [Fact]
    public void Moved_is_listed_once_per_entity()
    {
        var o = new EntityOverrides(
            [Overrides("phb14.rule.caasting-time").Overrides[0],
             Overrides("phb14.rule.caasting-time").Overrides[0] with { Field = "fields.y" }],
            []);
        var r = OverrideMigration.Migrate(o, [Ent("phb14.rule.caasting-time")], [Ent("phb14.rule.casting-time")]);

        r.Moved.Should().HaveCount(1);
        r.Overrides.Overrides.Should().OnlyContain(x => x.EntityId == "phb14.rule.casting-time");
    }

    [Fact]
    public void Name_similarity_is_one_minus_normalised_edit_distance()
    {
        OverrideMigration.NameSimilarity("casting-time", "casting-time").Should().Be(1.0);
        OverrideMigration.NameSimilarity("caasting-time", "casting-time").Should().BeApproximately(1 - 1.0 / 13, 1e-9);
        OverrideMigration.NameSimilarity("abc", "xyz").Should().Be(0.0);
    }

    [Fact]
    public void Declines_move_with_a_renamed_entity_and_orphans_are_listed()
    {
        var o = new EntityOverrides([], [],
            [new Decline("phb14.rule.caasting-time", "junk", At), new Decline("phb14.rule.vanished", "junk", At)]);
        var r = OverrideMigration.Migrate(o,
            [Ent("phb14.rule.caasting-time"), Ent("phb14.rule.vanished", page: 50)], [Ent("phb14.rule.casting-time")]);

        r.Overrides.Declines.Select(d => d.EntityId).Should().Equal("phb14.rule.casting-time", "phb14.rule.vanished");
        r.Overrides.Declines[0].Reason.Should().Be("junk");
        r.Orphaned.Should().Equal("phb14.rule.vanished");
    }
}
