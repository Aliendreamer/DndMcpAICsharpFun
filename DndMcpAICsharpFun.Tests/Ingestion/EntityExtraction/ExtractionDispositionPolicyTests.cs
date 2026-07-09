using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class ExtractionDispositionPolicyTests
{
    [Fact]
    public void Ungrounded_IsNeedsReview_EvenWhenConfidentAndClean()
    {
        // The core honesty fix: a confident, well-cased, but ungrounded extraction must NOT be accepted.
        ExtractionDispositionPolicy.Derive(grounded: false, name: "Fireball", confidence: "high")
            .Should().Be(EntityDisposition.NeedsReview);
    }

    [Fact]
    public void Grounded_Confident_CleanName_IsAccepted()
    {
        ExtractionDispositionPolicy.Derive(grounded: true, name: "Fireball", confidence: "high")
            .Should().Be(EntityDisposition.Accepted);
    }

    [Fact]
    public void Grounded_LowConfidence_IsNeedsReview()
    {
        ExtractionDispositionPolicy.Derive(grounded: true, name: "Fireball", confidence: "low")
            .Should().Be(EntityDisposition.NeedsReview);
    }

    [Fact]
    public void Grounded_OcrArtifactName_IsNeedsReview()
    {
        // All-caps name trips the OCR-artifact heuristic.
        ExtractionDispositionPolicy.Derive(grounded: true, name: "FIREBALL", confidence: "high")
            .Should().Be(EntityDisposition.NeedsReview);
    }

    [Fact]
    public void CanonicalEntityWithoutDisposition_DeserializesAsAccepted()
    {
        // Backward compatibility: pre-change canonical entities lack the field -> Accepted.
        const string json =
            """
            { "Id": "phb14.spell.fireball", "Type": "Spell", "Name": "Fireball",
              "SourceBook": "PHB", "Edition": "Edition2014", "Page": 241,
              "FirstAppearedIn": { "SourceBook": "PHB", "Edition": "Edition2014", "Page": 241 },
              "RevisedIn": [], "SettingTags": [], "CanonicalText": "", "Fields": {} }
            """;

        var envelope = JsonSerializer.Deserialize<EntityEnvelope>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });

        envelope!.Disposition.Should().Be(EntityDisposition.Accepted);
    }

    // ── Verdict overload (Task 6: shared GroundingCascade wiring) ──────────────────────────────

    [Fact]
    public void Verdict_Ungrounded_IsUngrounded_EvenWhenConfidentAndClean()
    {
        // Tier-2-judge-confirmed fabrication must NOT collapse into NeedsReview — it gets its own
        // disposition so it stays excluded from dnd_entities while remaining distinct from a
        // model-chosen Declined.
        var verdict = new GroundingVerdict(GroundingStatus.Ungrounded, DecidedByTier: 2, Score: 0.1);

        ExtractionDispositionPolicy.Derive(verdict, name: "Fireball", confidence: "high")
            .Should().Be(EntityDisposition.Ungrounded);
    }

    [Fact]
    public void Verdict_Uncertain_IsNeedsReview()
    {
        // No judge verdict (e.g. judge disabled at extraction time) → can't confirm ungrounded,
        // so it falls back to the existing NeedsReview signal rather than a harder rejection.
        var verdict = new GroundingVerdict(GroundingStatus.Uncertain, DecidedByTier: 1, Score: 0.5);

        ExtractionDispositionPolicy.Derive(verdict, name: "Fireball", confidence: "high")
            .Should().Be(EntityDisposition.NeedsReview);
    }

    [Fact]
    public void Verdict_Grounded_CleanNameConfidentConfidence_IsAccepted()
    {
        var verdict = new GroundingVerdict(GroundingStatus.Grounded, DecidedByTier: 0, Score: 1.0);

        ExtractionDispositionPolicy.Derive(verdict, name: "Fireball", confidence: "high")
            .Should().Be(EntityDisposition.Accepted);
    }

    [Fact]
    public void Verdict_Grounded_LowConfidence_IsNeedsReview()
    {
        // Grounded verdicts still run the existing name/confidence gate.
        var verdict = new GroundingVerdict(GroundingStatus.Grounded, DecidedByTier: 0, Score: 1.0);

        ExtractionDispositionPolicy.Derive(verdict, name: "Fireball", confidence: "low")
            .Should().Be(EntityDisposition.NeedsReview);
    }
}
