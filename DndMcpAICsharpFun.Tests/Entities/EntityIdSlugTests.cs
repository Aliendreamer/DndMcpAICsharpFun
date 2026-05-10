using DndMcpAICsharpFun.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class EntityIdSlugTests
{
    [Theory]
    [InlineData("Player's Handbook 2014", EntityType.Class,   "Fighter",     "phb14.class.fighter")]
    [InlineData("Monster Manual 2014",    EntityType.Monster, "Aboleth",     "mm14.monster.aboleth")]
    [InlineData("Tasha's Cauldron of Everything", EntityType.Subclass, "Swashbuckler", "tce.subclass.swashbuckler")]
    public void Generates_expected_slug(string book, EntityType type, string name, string expected)
    {
        EntityIdSlug.For(book, type, name).Should().Be(expected);
    }

    [Fact]
    public void Folds_non_ascii_characters()
    {
        EntityIdSlug.For("Player's Handbook 2014", EntityType.Spell, "Déjà Vu")
            .Should().Be("phb14.spell.deja-vu");
    }

    [Fact]
    public void Same_input_yields_same_slug()
    {
        var a = EntityIdSlug.For("Monster Manual 2014", EntityType.Monster, "Adult Red Dragon");
        var b = EntityIdSlug.For("Monster Manual 2014", EntityType.Monster, "Adult Red Dragon");
        a.Should().Be(b);
    }

    // Source key alias tests — both pipelines must produce the same slug
    [Theory]
    [InlineData("TCE",  "tce")]
    [InlineData("PHB",  "phb14")]
    [InlineData("DMG",  "dmg14")]
    [InlineData("XPHB", "phb24")]
    [InlineData("XDMG", "dmg24")]
    [InlineData("MM",   "mm14")]
    [InlineData("MM25", "mm24")]
    [InlineData("XGTE", "xgte")]
    [InlineData("MPMM", "mpmm")]
    [InlineData("VGM",  "vgm")]
    [InlineData("ERLW", "erlw")]
    public void Source_key_produces_expected_book_prefix(string sourceKey, string expectedPrefix)
    {
        var id = EntityIdSlug.For(sourceKey, EntityType.Class, "Fighter");
        id.Should().StartWith(expectedPrefix + ".");
    }

    [Fact]
    public void TCE_source_key_and_display_name_produce_same_prefix()
    {
        var fromKey  = EntityIdSlug.For("TCE",                            EntityType.Subclass, "Circle of Spores");
        var fromName = EntityIdSlug.For("Tasha's Cauldron of Everything", EntityType.Subclass, "Circle of Spores");
        fromKey.Should().Be(fromName);
    }
}
