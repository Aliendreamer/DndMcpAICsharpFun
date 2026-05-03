using DndMcpAICsharpFun.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class EntityIdSlugTests
{
    [Theory]
    [InlineData("Player's Handbook 2014", EntityType.Class, "Fighter",   "phb14.class.fighter")]
    [InlineData("Monster Manual 2014",    EntityType.Monster, "Aboleth", "mm14.monster.aboleth")]
    [InlineData("Tasha's Cauldron of Everything", EntityType.Subclass, "Swashbuckler", "tasha.subclass.swashbuckler")]
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
}
