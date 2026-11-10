using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Programmatic;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Programmatic;

public class LeadInLoreTests
{
    private static EntityEnvelope Lore(EntityType type, params string[] entries) => new(
        "bgg.lore.x", type, "X", "BGG", "2014", 11,
        new FirstAppearance("BGG", "2014", 11), [], [], "X",
        JsonSerializer.SerializeToElement(new { entries }),
        DataSource: DataSources.Programmatic,
        Checks: [new EntityCheck("table-cut", false, "lead-in")]);

    [Theory]
    [InlineData("This section presents one new subclass option: the Path of the Giant for the barbarian.")]
    [InlineData("Here are sample adventure hooks to bring characters to the Ashen Grotto or to give them missions to pursue there.")]
    [InlineData("The spells are presented in alphabetical order.")]
    [InlineData("Use the First-Year Encounters table for random encounters on campus during this adventure.")]
    [InlineData("Here's what you need to know to run the adventure in this chapter.")]
    [InlineData("A menagerie of deadly monsters for the world's greatest roleplaying game")]
    public void Introduction_or_blurb_is_declined(string text)
    {
        LeadInLore.Is(Lore(EntityType.Lore, text)).Should().BeTrue();
        var e = OverrideMerger.SetDisposition(Lore(EntityType.Lore, text), null);
        e.Disposition.Should().Be(EntityDisposition.Declined);
        e.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public void A_one_sentence_rule_is_kept()
    {
        var e = Lore(EntityType.Lore, "You gain one 9th-level spell slot, provided that you already have one.");
        LeadInLore.Is(e).Should().BeFalse();
        OverrideMerger.SetDisposition(e, null).Disposition.Should().Be(EntityDisposition.NeedsReview);
    }

    [Fact]
    public void An_intro_phrase_deep_inside_a_rule_is_kept()
    {
        var e = Lore(EntityType.Lore, "Prerequisite: 6th-level artificer Item: A suit of armor (requires attunement) While wearing this " +
            "armor, a creature has resistance to one of the following damage types, which you choose when you infuse the item: acid, cold, or fire.");
        LeadInLore.Is(e).Should().BeFalse();
    }

    [Fact]
    public void More_than_one_sentence_or_entry_is_kept()
    {
        LeadInLore.Is(Lore(EntityType.Lore, "Here are the hooks. Giants stomp the town.")).Should().BeFalse();
        LeadInLore.Is(Lore(EntityType.Lore, "Here are the hooks.", "Giants stomp the town.")).Should().BeFalse();
    }

    [Fact]
    public void Only_Lore_is_declined()
    {
        LeadInLore.Is(Lore(EntityType.Rule, "The spells are presented in alphabetical order.")).Should().BeFalse();
    }
}
