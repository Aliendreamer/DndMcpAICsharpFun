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
}
