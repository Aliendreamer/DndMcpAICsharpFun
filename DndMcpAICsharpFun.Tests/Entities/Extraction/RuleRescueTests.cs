using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

// extraction-content-classification Phase 1 (Rule): a decline-bound candidate (gated prior, no
// 5etools match) with substantial prose is rescued as EntityType.Rule instead of being declined.
public sealed class RuleRescueTests
{
    [Fact]
    public void RuleSignature_true_for_substantial_prose()
    {
        var candidate = new EntityCandidate(
            EntityType.Background, "Chase Complications", new string('x', 400), 100);

        ExtractionSignatures.RuleSignature(candidate).Should().BeTrue();
    }

    [Fact]
    public void RuleSignature_false_for_short_fragment()
    {
        var candidate = new EntityCandidate(EntityType.Background, "Blah", "too short", 1);

        ExtractionSignatures.RuleSignature(candidate).Should().BeFalse();
    }

    [Fact]
    public void RescueAsRuleOrNull_rebinds_TypePrior_to_Rule_when_decline_bound_and_substantial()
    {
        var candidate = new EntityCandidate(
            EntityType.Background, "Chase Complications", new string('x', 400), 100,
            TypePrior: new[] { EntityType.Background });

        var rescued = EntityExtractionOrchestrator.RescueAsRuleOrNull(candidate, DeterministicOutcome.Decline);

        rescued.Should().NotBeNull();
        rescued!.TypePrior.Should().BeEquivalentTo(new[] { EntityType.Rule });
        // The original gated prior must NOT be re-offered alongside Rule.
        rescued.TypePrior.Should().NotContain(EntityType.Background);
        // The stable keyword-derived Type field (used for id slugging) is untouched.
        rescued.Type.Should().Be(EntityType.Background);
    }

    [Theory]
    [InlineData(DeterministicOutcome.Defer)]
    [InlineData(DeterministicOutcome.ForceType)]
    [InlineData(DeterministicOutcome.Drop)]
    public void RescueAsRuleOrNull_returns_null_for_non_decline_outcomes(DeterministicOutcome outcome)
    {
        var candidate = new EntityCandidate(
            EntityType.Background, "Chase Complications", new string('x', 400), 100,
            TypePrior: new[] { EntityType.Background });

        EntityExtractionOrchestrator.RescueAsRuleOrNull(candidate, outcome).Should().BeNull();
    }

    [Fact]
    public void RescueAsRuleOrNull_returns_null_for_decline_without_rule_signature()
    {
        var candidate = new EntityCandidate(
            EntityType.Background, "Blah", "too short", 1,
            TypePrior: new[] { EntityType.Background });

        EntityExtractionOrchestrator.RescueAsRuleOrNull(candidate, DeterministicOutcome.Decline).Should().BeNull();
    }
}