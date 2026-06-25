using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class ExtractionCandidateDeduplicatorTests
{
    private static EntityCandidate C(string name, string text) =>
        new(EntityType.Monster, name, text, 1, new[] { EntityType.Monster });

    [Fact]
    public void Prefers_candidate_with_stat_block_over_lore_only()
    {
        // Headerless monster: the section candidate is lore-only; the stat-block candidate carries
        // the block. The block must win so the monster types instead of declining.
        var lore = C("Cyclops", "Cyclopes are reclusive giants who value gold and shells.");
        var stat = C("Cyclops", "Huge giant, chaotic neutral  Armor Class 14  Hit Points 138 (12d12 + 60)");

        ExtractionCandidateDeduplicator.Dedupe(new[] { lore, stat }, "Monster Manual")
            .Should().ContainSingle().Which.Text.Should().Contain("Armor Class");
    }

    [Fact]
    public void Among_stat_blocks_prefers_richer_section_text()
    {
        // Header-clean monster: both an isolated stat block and the full section have the block.
        // The richer section (name + lore + block) must win — the isolated block is what the model
        // sometimes declines (the Aboleth/Bugbear regression).
        var isolated = C("Aboleth", "Large aberration, lawful evil  Armor Class 17  Hit Points 135");
        var section = C("Aboleth",
            "ABOLETH  Aboleths are ancient horrors of the deep.  Large aberration, lawful evil  " +
            "Armor Class 17  Hit Points 135  Telepathy 120 ft.");

        ExtractionCandidateDeduplicator.Dedupe(new[] { isolated, section }, "Monster Manual")
            .Should().ContainSingle().Which.Text.Should().Contain("ancient horrors");
    }

    [Fact]
    public void Keeps_distinct_entities()
    {
        var a = C("Goblin", "Small humanoid  Armor Class 15  Hit Points 7");
        var b = C("Orc", "Medium humanoid  Armor Class 13  Hit Points 15");

        ExtractionCandidateDeduplicator.Dedupe(new[] { a, b }, "MM").Should().HaveCount(2);
    }
}
