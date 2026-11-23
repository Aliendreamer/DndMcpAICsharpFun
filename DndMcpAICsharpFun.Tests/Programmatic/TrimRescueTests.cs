using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Programmatic;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Programmatic;

public class TrimRescueTests
{
    private const string Lore1 = "Long lore paragraph line one that is long enough";
    private const string Lore2 = "and its continuation line that is long enough";

    private static EntityEnvelope Ent(string id, EntityType type, string name, string text,
        EntityDisposition disp = EntityDisposition.Accepted) => new(
        id, type, name, "MM", "2014", 14, new FirstAppearance("MM", "2014", 14), [], [], text,
        JsonDocument.Parse("{}").RootElement.Clone(), Disposition: disp);

    private static EntityEnvelope Host(string text, EntityDisposition disp = EntityDisposition.Accepted) =>
        Ent("mm14.monster.x", EntityType.Monster, "X", text, disp);

    private static IReadOnlyList<EntityEnvelope> Run(string baseText, EntityEnvelope host, params EntityEnvelope[] others) =>
        TrimRescue.Compute([(host, baseText)], [host, .. others]);

    [Fact]
    public void Removed_lore_held_nowhere_becomes_a_flagged_lore_rescue()
    {
        var r = Run($"X\nStat line\n{Lore1}\n{Lore2}", Host("X\nStat line")).Should().ContainSingle().Subject;
        r.Id.Should().Be("mm14.lore.x-trimmed");
        r.Name.Should().Be("X (trimmed text)");
        r.Type.Should().Be(EntityType.Lore);
        r.DataSource.Should().Be(DataSources.Programmatic);
        var c = r.Checks.Should().ContainSingle().Subject;
        c.Name.Should().Be("text-complete");
        c.Passed.Should().BeFalse();
        c.Reason.Should().Contain("mm14.monster.x");
        var entries = r.Fields.GetProperty("entries");
        entries.GetArrayLength().Should().Be(1);
        entries[0].GetString().Should().Be($"{Lore1} {Lore2}");
    }

    [Fact]
    public void Removed_lines_held_by_another_entity_are_not_rescued()
    {
        var other = Ent("mm14.lore.y", EntityType.Lore, "Y", $"Y\n{Lore1} {Lore2}");
        Run($"X\nStat line\n{Lore1}\n{Lore2}", Host("X\nStat line"), other).Should().BeEmpty();
    }

    [Fact]
    public void Typo_fix_only_creates_no_rescue()
    {
        Run("Teh dragon sleeps under the mountain for a hundred years",
            Host("The dragon sleeps under the mountain for a hundred years")).Should().BeEmpty();
    }

    [Fact]
    public void Short_label_only_creates_no_rescue()
    {
        Run("X\nCHAPTER 3 | DOMAINS", Host("X")).Should().BeEmpty();
    }

    [Fact]
    public void Heading_above_rescued_text_becomes_the_name()
    {
        var r = Run($"X\nStat line\nGiant Gods\n{Lore1}", Host("X\nStat line")).Should().ContainSingle().Subject;
        r.Name.Should().Be("Giant Gods");
        r.CanonicalText.Should().Be($"Giant Gods\n{Lore1}");
    }

    [Fact]
    public void Declined_host_gets_no_rescue()
    {
        Run($"X\n{Lore1}", Host("X", EntityDisposition.Declined)).Should().BeEmpty();
    }

    [Fact]
    public void Hyphenated_line_break_joins_without_hyphen()
    {
        var r = Run("X\nThe ancient red dragon hoards its trea-\nsure beyond the mountain far away from any town", Host("X"))
            .Should().ContainSingle().Subject;
        r.Fields.GetProperty("entries")[0].GetString()
            .Should().Be("The ancient red dragon hoards its treasure beyond the mountain far away from any town");
    }

    [Fact]
    public void Rescue_id_gets_suffix_2_on_clash()
    {
        var clash = Ent("mm14.lore.x-trimmed", EntityType.Lore, "Z", "Z");
        var r = Run($"X\n{Lore1}", Host("X"), clash).Should().ContainSingle().Subject;
        r.Id.Should().Be("mm14.lore.x-trimmed-2");
        TrimRescue.IsRescueId(r.Id).Should().BeTrue();
    }

    [Theory]
    [InlineData("Challenge 3 (700 XP)")]
    [InlineData("Languages \u2014")]
    public void Stat_block_lines_above_do_not_become_the_name(string above)
    {
        var r = Run($"X\n{above}\n{Lore1}", Host("X")).Should().ContainSingle().Subject;
        r.Name.Should().Be("X (trimmed text)");
    }

    [Fact]
    public void Kept_heading_like_line_above_belongs_to_the_host()
    {
        var r = Run($"X\nStat Line\n{Lore1}", Host("X\nStat Line")).Should().ContainSingle().Subject;
        r.Name.Should().Be("X (trimmed text)");
    }

    [Fact]
    public void Clash_suffix_loops()
    {
        TrimRescue.RescueId("mm14.monster.x", new HashSet<string> { "mm14.lore.x-trimmed", "mm14.lore.x-trimmed-2" })
            .Should().Be("mm14.lore.x-trimmed-3");
        TrimRescue.IsRescueId("mm14.lore.x-trimmed-3").Should().BeTrue();
    }

    [Fact]
    public void Two_non_consecutive_runs_become_two_entries()
    {
        var r = Run($"X\n{Lore1}\nKept short\n{Lore2}", Host("X\nKept short")).Should().ContainSingle().Subject;
        r.Fields.GetProperty("entries").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void Declined_entity_text_does_not_count_as_held()
    {
        var other = Ent("mm14.lore.y", EntityType.Lore, "Y", $"{Lore1}", EntityDisposition.Declined);
        Run($"X\n{Lore1}", Host("X"), other).Should().ContainSingle();
    }

    [Fact]
    public void Rescue_id_entity_text_does_not_count_as_held()
    {
        var other = Ent("mm14.lore.y-trimmed", EntityType.Lore, "Y", $"{Lore1}");
        Run($"X\n{Lore1}", Host("X"), other).Should().ContainSingle();
    }
}
