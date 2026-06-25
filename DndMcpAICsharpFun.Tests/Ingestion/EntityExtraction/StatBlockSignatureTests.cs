using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class StatBlockSignatureTests
{
    [Fact]
    public void Complete_stat_block_is_recognized()
    {
        const string aboleth =
            "Large aberration, lawful evil  Armor Class 17 (natural armor)  Hit Points 135 (18d10 + 36)  " +
            "STR 21 DEX 9 CON 15  Challenge 10 (5,900 XP)";
        StatBlockSignature.IsCompleteStatBlock(aboleth).Should().BeTrue();
    }

    [Fact]
    public void Lore_text_is_not_a_stat_block()
    {
        StatBlockSignature.IsCompleteStatBlock(
            "Aboleths are ancient horrors of the deep that enslave lesser creatures.").Should().BeFalse();
    }

    [Fact]
    public void Ac_and_hp_without_challenge_is_not_a_creature_stat_block()
    {
        // A non-creature reference (e.g. a vehicle/object) without "Challenge" must not match.
        StatBlockSignature.IsCompleteStatBlock("Armor Class 19  Hit Points 50  damage threshold 15")
            .Should().BeFalse();
    }

    [Fact]
    public void Empty_is_not_a_stat_block()
    {
        StatBlockSignature.IsCompleteStatBlock("").Should().BeFalse();
        StatBlockSignature.IsCompleteStatBlock(null!).Should().BeFalse();
    }
}
