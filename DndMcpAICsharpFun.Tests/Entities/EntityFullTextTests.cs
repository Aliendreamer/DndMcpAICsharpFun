using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public sealed class EntityFullTextTests
{
    private static string Render(EntityType type, string json, string name = "Thing") =>
        new EntityCanonicalTextDispatcher().Render(new EntityEnvelope(
            Id: $"test.{type}.thing", Type: type, Name: name, SourceBook: "TEST", Edition: "Edition2014", Page: null,
            FirstAppearedIn: new FirstAppearance("TEST", "Edition2014", null), RevisedIn: [], SettingTags: [],
            CanonicalText: "", Fields: JsonDocument.Parse(json).RootElement));

    [Theory]
    [InlineData(EntityType.Race)]
    [InlineData(EntityType.Subrace)]
    [InlineData(EntityType.Background)]
    [InlineData(EntityType.Feat)]
    [InlineData(EntityType.Condition)]
    [InlineData(EntityType.Lore)]
    [InlineData(EntityType.Rule)]
    [InlineData(EntityType.MagicItem)]
    [InlineData(EntityType.Subclass)]
    [InlineData(EntityType.Class)]
    [InlineData(EntityType.God)]
    [InlineData(EntityType.Plane)]
    [InlineData(EntityType.Location)]
    [InlineData(EntityType.Faction)]
    [InlineData(EntityType.Trap)]
    public void Every_paragraph_and_named_block_is_in_the_text(EntityType type)
    {
        var text = Render(type, """
            { "className": "Fighter", "entries": [ "First paragraph.", "Second paragraph with {@condition prone}.",
                { "type": "entries", "name": "Darkvision", "entries": [ "You see in the dark.",
                  { "type": "list", "items": [ "Item one.", { "name": "Two", "entries": ["Item two."] } ] } ] },
                { "type": "table", "caption": "Madness", "colLabels": ["d8", "Effect"], "rows": [ ["1", "Screams"], ["2", "Laughs"] ] } ] }
            """);
        text.Should().Contain("First paragraph.").And.Contain("Second paragraph with prone.")
            .And.Contain("Darkvision. You see in the dark.").And.Contain("Item one.").And.Contain("Two. Item two.")
            .And.Contain("Madness").And.Contain("d8 | Effect").And.Contain("1 | Screams").And.Contain("2 | Laughs");
    }

    [Fact]
    public void A_paragraph_the_renderer_already_shows_is_not_repeated()
    {
        var text = Render(EntityType.Lore, """{ "category": "Planes", "entries": ["Only paragraph.", "Next."] }""");
        text.Split("Only paragraph.").Should().HaveCount(2);
        text.Should().StartWith("Thing. Planes lore. Only paragraph.");
    }

    [Fact]
    public void A_gods_group_is_kept_and_a_lore_category_is_not_repeated()
    {
        Render(EntityType.God, """{ "pantheon": "Elven", "category": "The Seldarine" }""").Should().Contain("Category: The Seldarine");
        Render(EntityType.Lore, """{ "category": "Planes", "entries": ["Text."] }""").Split("Planes").Should().HaveCount(2);
    }

    [Fact]
    public void Text_outside_entries_is_labelled_and_kept()
    {
        var text = Render(EntityType.Trap, """
            { "trapHazType": "mechanical", "entries": ["A pit."], "trigger": ["A creature steps on it."],
              "countermeasures": ["DC 15 Perception."], "source": "DMG", "page": 12, "_version": { "name": "x" } }
            """);
        text.Should().Contain("Trigger: A creature steps on it.").And.Contain("Countermeasures: DC 15 Perception.")
            .And.NotContain("DMG").And.NotContain("Version");
    }

    [Fact]
    public void Short_value_lists_stay_on_one_line()
    {
        Render(EntityType.Class, """{ "startingProficiencies": { "weapons": ["daggers", "darts"] }, "entries": ["Text."] }""")
            .Should().Contain("Starting proficiencies: daggers, darts");
    }

    private static string RenderPrinted(EntityType type, string dataSource, string printed, string json) =>
        new EntityCanonicalTextDispatcher().Render(new EntityEnvelope(
            Id: "test.x.acolyte", Type: type, Name: "Acolyte", SourceBook: "TEST", Edition: "Edition2014", Page: null,
            FirstAppearedIn: new FirstAppearance("TEST", "Edition2014", null), RevisedIn: [], SettingTags: [],
            CanonicalText: printed, Fields: JsonDocument.Parse(json).RootElement, DataSource: dataSource));

    [Fact]
    public void Printed_lines_the_fields_lack_are_appended_as_paragraphs()
    {
        var text = RenderPrinted(EntityType.Background, DataSources.Programmatic,
            "ACOLYTE\nYou have spent your life in the ser-\nvice of a temple to a god.\nFeature: Shelter. You command\nrespect from the faithful.\nA closing line of prose here.",
            """{ "entries": ["Feature: Shelter. You command respect from the faithful."] }""");
        text.Should().Contain("You have spent your life in the service of a temple to a god.")
            .And.Contain("A closing line of prose here.");
        text.Split("You command respect").Should().HaveCount(2, "a printed line the fields hold is not repeated");
    }

    [Fact]
    public void A_sentences_short_last_line_is_kept_although_its_words_stand_elsewhere()
    {
        RenderPrinted(EntityType.Subrace, DataSources.Programmatic,
            "Ability Score Increase. Your Wisdom score\nincreases by 1.",
            """{ "entries": ["Your hit point maximum increases by 1."] }""")
            .Should().Contain("Ability Score Increase. Your Wisdom score increases by 1.");
    }

    [Theory]
    [InlineData(EntityType.Background, "llm")]
    [InlineData(EntityType.Monster, "programmatic")]
    public void Printed_lines_are_not_appended_for_other_sources_or_monsters(EntityType type, string dataSource)
    {
        RenderPrinted(type, dataSource, "A printed line only here.", """{ "size": ["M"], "cr": "1", "entries": ["Text."] }""")
            .Should().NotContain("A printed line only here.");
    }

    [Fact]
    public void Spell_tables_and_named_blocks_are_kept()
    {
        var text = Render(EntityType.Spell, """
            { "level": 1, "school": "V", "entries": [ "A storm.", { "type": "table", "rows": [ ["1-2", "Fire"] ] },
              { "type": "entries", "name": "Aftermath", "entries": ["Smoke."] } ] }
            """);
        text.Should().Contain("A storm.").And.Contain("1-2 | Fire").And.Contain("Aftermath").And.Contain("Smoke.");
    }

    [Theory]
    [InlineData("""{ "entries": "just text" }""")]
    [InlineData("""{ "entries": [null, 5, {}, []] }""")]
    [InlineData("""{ "entries": { "a": 1 } }""")]
    [InlineData("""[]""")]
    public void Odd_shapes_never_throw(string json) => Render(EntityType.Lore, json).Should().Contain("Thing");
}