using DndMcpAICsharpFun.Features.Retrieval.Entities;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Entities.Retrieval;

public sealed class SpellClassIndexTests
{
    private const string Fixture = """
    {
      "PHB": {
        "Fireball":    { "class": [ { "name": "Sorcerer", "source": "PHB" }, { "name": "Wizard", "source": "PHB" } ] },
        "Mage Armor":  { "class": [ { "name": "Wizard", "source": "PHB" } ] },
        "Cure Wounds": { "class": [ { "name": "Cleric", "source": "PHB" } ] }
      }
    }
    """;

    private static string WriteFixture(string json)
    {
        var dir = Directory.CreateTempSubdirectory("scix");
        var spells = Directory.CreateDirectory(Path.Combine(dir.FullName, "spells"));
        File.WriteAllText(Path.Combine(spells.FullName, "sources.json"), json);
        return dir.FullName;
    }

    [Fact]
    public void ClassesFor_returns_the_casting_classes()
    {
        var dir = WriteFixture(Fixture);
        try
        {
            var idx = new SpellClassIndex(dir);
            idx.ClassesFor("Fireball", "PHB").Should().BeEquivalentTo("Sorcerer", "Wizard");
            idx.CanCast("Wizard", "Fireball", "PHB").Should().BeTrue();
            idx.CanCast("Cleric", "Fireball", "PHB").Should().BeFalse();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Matching_is_normalization_insensitive_for_name_source_and_class()
    {
        var dir = WriteFixture(Fixture);
        try
        {
            // punctuation / case / spacing differences on all three inputs still resolve
            new SpellClassIndex(dir).CanCast("wizard", "fire ball!", "phb").Should().BeTrue();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Unknown_spell_returns_empty()
    {
        var dir = WriteFixture(Fixture);
        try
        {
            new SpellClassIndex(dir).ClassesFor("Nonexistent Spell", "PHB").Should().BeEmpty();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Missing_index_file_yields_empty_index_without_throwing()
    {
        var idx = new SpellClassIndex(Path.Combine(Path.GetTempPath(), "no-such-5etools-dir-xyz"));
        idx.ClassesFor("Fireball", "PHB").Should().BeEmpty();
        idx.CanCast("Wizard", "Fireball", "PHB").Should().BeFalse();
    }
}
