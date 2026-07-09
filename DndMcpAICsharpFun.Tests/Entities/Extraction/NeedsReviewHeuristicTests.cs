using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public class NeedsReviewHeuristicTests
{
    [Theory]
    [InlineData("Circle of Spores", false)]
    [InlineData("Fireball", false)]
    [InlineData("Tasha's Cauldron of Everything", false)]
    public void Clean_names_do_not_trigger_heuristic(string name, bool expected)
        => ExtractionNeedsReview.HasOcrArtifacts(name).Should().Be(expected);

    [Theory]
    [InlineData("CIRCLE OF SPORES", true)]
    [InlineData("Path of the Beast f eature", true)]
    [InlineData("Gons OF YouR WoRLD", true)]
    [InlineData("Some ..... Thing", true)]
    public void Artifact_names_trigger_heuristic(string name, bool expected)
        => ExtractionNeedsReview.HasOcrArtifacts(name).Should().Be(expected);

    [Fact]
    public void Low_confidence_sets_needs_review_regardless_of_name()
        => ExtractionNeedsReview.Derive("Circle of Spores", "low").Should().BeTrue();

    [Fact]
    public void Medium_confidence_sets_needs_review_regardless_of_name()
        => ExtractionNeedsReview.Derive("Fireball", "medium").Should().BeTrue();

    [Fact]
    public void High_confidence_clean_name_does_not_set_needs_review()
        => ExtractionNeedsReview.Derive("Circle of Spores", "high").Should().BeFalse();

    [Fact]
    public void High_confidence_artifact_name_still_sets_needs_review()
        => ExtractionNeedsReview.Derive("CIRCLE OF SPORES", "high").Should().BeTrue();

    [Fact]
    public void Null_confidence_uses_heuristic_only()
        => ExtractionNeedsReview.Derive("Circle of Spores", null).Should().BeFalse();
}