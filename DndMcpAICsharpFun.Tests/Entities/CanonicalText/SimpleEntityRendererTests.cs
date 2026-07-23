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

    // Live-validation regression: Bountiful Luck's canonicalText contained the literal
    // "Prerequisite: [{"race":[{"name":"halfling"}]}]" — the 5etools "prerequisite" array was
    // dumped as raw JSON instead of rendered as prose. FeatCanonicalTextRenderer must translate
    // the known 5etools prerequisite shapes (race/ability/spellcasting/level/proficiency/other)
    // into readable text and never leak a raw '{' or '[' into canonicalText.
    [Fact]
    public void Feat_prerequisite_race_renders_as_prose_not_raw_json()
    {
        var fields = J("{\"prerequisite\":[{\"race\":[{\"name\":\"halfling\"}]}],\"entries\":[\"You are unfailingly lucky.\"]}");
        var text = new FeatCanonicalTextRenderer().Render("Bountiful Luck", fields);
        text.Should().Contain("halfling");
        text.Should().NotContainAny("{", "[");
    }

    [Fact]
    public void Feat_prerequisite_ability_dex_renders_as_prose_not_raw_json()
    {
        var fields = J("{\"prerequisite\":[{\"ability\":[{\"dex\":13}]}],\"entries\":[\"You are agile.\"]}");
        var text = new FeatCanonicalTextRenderer().Render("Fencer", fields);
        text.Should().Contain("Dexterity 13");
        text.Should().NotContainAny("{", "[");
    }

    [Fact]
    public void Feat_prerequisite_ability_str_renders_as_prose_not_raw_json()
    {
        var fields = J("{\"prerequisite\":[{\"ability\":[{\"str\":13}]}],\"entries\":[\"You are strong.\"]}");
        var text = new FeatCanonicalTextRenderer().Render("Brawler", fields);
        text.Should().Contain("Strength 13");
        text.Should().NotContainAny("{", "[");
    }

    [Fact]
    public void Feat_prerequisite_spellcasting_renders_as_prose_not_raw_json()
    {
        var fields = J("{\"prerequisite\":[{\"spellcasting\":true}],\"entries\":[\"You have expanded your magical power.\"]}");
        var text = new FeatCanonicalTextRenderer().Render("Metamagic Adept", fields);
        text.Should().Contain("The ability to cast at least one spell");
        text.Should().NotContainAny("{", "[");
    }

    [Fact]
    public void Feat_prerequisite_unknown_shape_degrades_gracefully_without_raw_json_or_throw()
    {
        var fields = J("{\"prerequisite\":[{\"someUnknownKey\":{\"weird\":[1,2,3]}}],\"entries\":[\"Something odd.\"]}");
        var text = new FeatCanonicalTextRenderer().Render("Odd Feat", fields);
        text.Should().Contain("Odd Feat");
        text.Should().NotContainAny("{", "[");
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


    // Live-validation regression: JsonElement.TryGetInt32 THROWS InvalidOperationException when
    // the element's ValueKind is not Number (it only returns false for overflow/format issues on
    // an actual Number) — so a backfilled "value": null (e.g. Sling Bullet, Trinket) made
    // ItemCanonicalTextRenderer throw, and the throwing renderer caused the entity to be SKIPPED
    // out of dnd_entities entirely (EntityIngestionOrchestrator catches the render exception and
    // continues). A canonicalText renderer must never throw for any Fields JSON shape.
    [Fact]
    public void Item_renderer_with_null_value_does_not_throw()
    {
        var fields = J("{\"type\":\"A\",\"value\":null,\"entries\":[\"Ammunition for a sling.\"]}");
        var text = new ItemCanonicalTextRenderer().Render("Sling Bullet", fields);
        text.Should().Contain("Sling Bullet").And.Contain("Ammunition for a sling").And.NotContain("Value:");
    }

    [Fact]
    public void Item_renderer_with_missing_value_does_not_throw()
    {
        var fields = J("{\"type\":\"TG\",\"entries\":[\"A curious little object.\"]}");
        var text = new ItemCanonicalTextRenderer().Render("Trinket", fields);
        text.Should().Contain("Trinket").And.Contain("curious little object").And.NotContain("Value:");
    }

    [Fact]
    public void Armor_renderer_with_null_ac_does_not_throw()
    {
        var fields = J("{\"type\":\"LA\",\"ac\":null,\"entries\":[\"Padded armor consists of quilted layers of cloth.\"]}");
        var text = new ArmorCanonicalTextRenderer().Render("Padded", fields);
        text.Should().Contain("Padded").And.Contain("quilted layers").And.NotContain("AC:");
    }

    [Fact]
    public void Armor_renderer_with_missing_ac_does_not_throw()
    {
        var fields = J("{\"type\":\"LA\",\"entries\":[\"Some armor entry.\"]}");
        var text = new ArmorCanonicalTextRenderer().Render("Test Armor", fields);
        text.Should().Contain("Test Armor").And.NotContain("AC:");
    }
}