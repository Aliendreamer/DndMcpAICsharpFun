using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Retrieval.Entities.Dedup;

public sealed class EntityDedupKeyTests
{
    private static EntityEnvelope Env(string id, string name, EntityType type, string edition) =>
        TestEnvelopes.Make(id: id, name: name, type: type, edition: edition);

    [Fact]
    public void Same_name_type_edition_different_id_share_key()
    {
        var a = Env("phb14.spell.fireball", "Fireball", EntityType.Spell, "Edition2014");
        var b = Env("dmg14.spell.fireball", "FIRE BALL", EntityType.Spell, "Edition2014");
        EntityDedupKey.From(a).Should().Be(EntityDedupKey.From(b));
    }

    [Fact]
    public void Different_edition_does_not_share_key()
    {
        var a = Env("phb14.spell.fireball", "Fireball", EntityType.Spell, "Edition2014");
        var b = Env("phb24.spell.fireball", "Fireball", EntityType.Spell, "Edition2024");
        EntityDedupKey.From(a).Should().NotBe(EntityDedupKey.From(b));
    }

    [Fact]
    public void Different_type_does_not_share_key()
    {
        var a = Env("x.race.dwarf", "Dwarf", EntityType.Race, "Edition2014");
        var b = Env("x.monster.dwarf", "Dwarf", EntityType.Monster, "Edition2014");
        EntityDedupKey.From(a).Should().NotBe(EntityDedupKey.From(b));
    }
}
