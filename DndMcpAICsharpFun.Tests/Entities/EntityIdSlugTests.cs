using System.Text;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Infrastructure;
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

    /// <summary>
    /// Regression lock for the UUID v5 determinism that prevents orphaned Qdrant points.
    /// The expected value was computed once from the algorithm and hardcoded here so that
    /// any future change to the hashing logic (byte-swap order, version/variant bits, etc.)
    /// is immediately caught.
    /// Namespace: 6ba7b810-9dad-11d1-80b4-00c04fd430c8 (DNS namespace, RFC 4122 §C)
    /// Name:      UTF-8 bytes of "phb14.class.fighter"
    /// </summary>
    [Fact]
    public void UuidV5_produces_known_deterministic_value_for_entity_id()
    {
        // Arrange
        var ns = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        var entityId = EntityIdSlug.For("PHB", EntityType.Class, "Fighter"); // == "phb14.class.fighter"
        entityId.Should().Be("phb14.class.fighter"); // guard: slug must be stable too

        var nameBytes = Encoding.UTF8.GetBytes(entityId);
        var expected  = new Guid("ad8c7b93-b71f-54c1-bbb3-4ccb445a2d18");

        // Act
        var actual = UuidV5.Create(ns, nameBytes);

        // Assert — if this changes, re-ingest ALL books before merging
        actual.Should().Be(expected,
            because: "Qdrant point IDs are derived from this UUID; any change creates orphaned points");
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
