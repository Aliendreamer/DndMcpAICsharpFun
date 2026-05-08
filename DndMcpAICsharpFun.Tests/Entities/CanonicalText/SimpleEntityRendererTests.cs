using System.Text.Json;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.CanonicalText;

public class SimpleEntityRendererTests
{
    private static JsonElement J(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    [Fact]
    public void Race_renderer_includes_size_and_speed()
    {
        var fields = J("{\"size\":[\"M\"],\"speed\":30,\"traitTags\":[\"Darkvision\"]}");
        var text = new RaceCanonicalTextRenderer().Render("Human", fields);
        text.Should().Contain("Medium").And.Contain("30 ft").And.Contain("Darkvision");
    }

    [Fact]
    public void Background_renderer_includes_skill_proficiencies()
    {
        var fields = J("{\"skillProficiencies\":[{\"history\":true,\"insight\":true}],\"entries\":[\"A sage studies arcane lore.\"]}");
        var text = new BackgroundCanonicalTextRenderer().Render("Sage", fields);
        text.Should().Contain("Sage").And.Contain("history").And.Contain("insight");
    }

    [Fact]
    public void Feat_renderer_includes_prerequisite_and_entries()
    {
        var fields = J("{\"prerequisite\":[{\"level\":{\"level\":4}}],\"entries\":[\"You master the technique of fighting in two weapons.\"]}");
        var text = new FeatCanonicalTextRenderer().Render("Dual Wielder", fields);
        text.Should().Contain("Dual Wielder").And.Contain("fighting in two weapons");
    }

    [Fact]
    public void God_renderer_includes_domains_and_alignment()
    {
        var fields = J("{\"alignment\":[\"L\",\"G\"],\"domains\":[\"Life\",\"Light\"],\"symbol\":\"Sun\",\"pantheon\":\"Forgotten Realms\",\"entries\":[\"Lathander is the god of birth.\"]}");
        var text = new GodCanonicalTextRenderer().Render("Lathander", fields);
        text.Should().Contain("Forgotten Realms").And.Contain("Life").And.Contain("Lathander");
    }

    [Fact]
    public void Rule_renderer_includes_ruletype_and_entries()
    {
        var fields = J("{\"ruleType\":\"O\",\"entries\":[\"This optional rule lets players do something cool.\"]}");
        var text = new RuleCanonicalTextRenderer().Render("Flanking", fields);
        text.Should().Contain("Flanking").And.Contain("optional").And.Contain("cool");
    }

    [Fact]
    public void Condition_renderer_includes_entries()
    {
        var fields = J("{\"entries\":[\"A blinded creature cannot see.\",\"Attack rolls against the creature have advantage.\"]}");
        var text = new ConditionCanonicalTextRenderer().Render("Blinded", fields);
        text.Should().Contain("Blinded").And.Contain("cannot see");
    }

    [Fact]
    public void MagicItem_renderer_includes_rarity_and_attunement()
    {
        var fields = J("{\"type\":\"W\",\"rarity\":\"rare\",\"reqAttune\":true,\"entries\":[\"This wand crackles with electricity.\"]}");
        var text = new MagicItemCanonicalTextRenderer().Render("Wand of Lightning Bolts", fields);
        text.Should().Contain("rare").And.Contain("attunement").And.Contain("electricity");
    }
}
