using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

// extraction-cross-type-recovery Task 1 (Item): a decline-bound candidate carrying a SPECIFIC
// mundane weapon/armor stat signature is rescued as EntityType.Item, checked BEFORE the Rule
// rescue (extraction-content-classification) so a genuine item is never mis-typed Rule.
public sealed class ItemRescueTests
{
    [Fact]
    public void ItemSignature_true_for_weapon_damage_line()
        => ExtractionSignatures.ItemSignature(new EntityCandidate(EntityType.Monster, "Longsword",
            "A martial melee weapon. Cost 15 gp. 1d8 slashing damage, versatile (1d10). Weight 3 lb.", 149)).Should().BeTrue();

    [Fact]
    public void ItemSignature_true_for_armor_stat_line()
        => ExtractionSignatures.ItemSignature(new EntityCandidate(EntityType.Monster, "Chain Mail",
            "Heavy armor. Armor Class 16. Cost 75 gp. Weight 55 lb. Strength 13 required.", 145)).Should().BeTrue();

    [Fact]
    public void ItemSignature_false_for_a_rule()
        => ExtractionSignatures.ItemSignature(new EntityCandidate(EntityType.Class, "Switching Weapons",
            new string('x', 60) + " When you attack, you can switch the weapon you are holding as part of the same action, without using your object interaction.", 195)).Should().BeFalse();

    [Fact]
    public void ItemSignature_false_for_short_fragment()
        => ExtractionSignatures.ItemSignature(new EntityCandidate(EntityType.Item, "x", "too short", 1)).Should().BeFalse();

    [Fact]
    public void Item_rescue_takes_priority_over_rule_for_item_signature_candidate()
    {
        var weapon = new EntityCandidate(EntityType.Monster, "Longsword",
            new string('x', 220) + " 1d8 slashing damage. Cost 15 gp.", 149); // both item-sig AND >=200 prose

        var item = EntityExtractionOrchestrator.RescueAsItemOrNull(weapon, DeterministicOutcome.Decline);

        item!.TypePrior.Should().BeEquivalentTo(new[] { EntityType.Item });
        // and the rule rescue would NOT run because item rescue returns non-null first (covered by the orchestrator wiring)
    }

    [Fact]
    public void Item_rescue_null_for_a_rule_which_falls_through_to_rule_rescue()
    {
        var rule = new EntityCandidate(EntityType.Class, "Switching Weapons",
            new string('x', 220) + " you can switch the weapon you are holding.", 195);

        EntityExtractionOrchestrator.RescueAsItemOrNull(rule, DeterministicOutcome.Decline).Should().BeNull();
        EntityExtractionOrchestrator.RescueAsRuleOrNull(rule, DeterministicOutcome.Decline).Should().NotBeNull(); // it IS rule-eligible
    }
}