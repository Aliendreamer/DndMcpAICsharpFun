using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class GroundingActionPolicyTests
{
    private static GroundingVerdict Verdict(GroundingStatus status) =>
        new(status, DecidedByTier: 0, Score: 1.0);

    [Fact]
    public void Grounded_with_clean_name_is_promoted()
    {
        // "Owlbear" has no OCR artifacts.
        ExtractionNeedsReview.HasOcrArtifacts("Owlbear").Should().BeFalse();

        var action = GroundingActionPolicy.Decide(Verdict(GroundingStatus.Grounded), "Owlbear");

        action.Should().Be(GroundingAction.Promote);
    }

    [Fact]
    public void Grounded_with_ocr_artifact_name_is_left_flagged()
    {
        // "Owl b ear" is an OCR-split rendering of "Owlbear" (lone single-letter word
        // before another word) and genuinely trips HasOcrArtifacts's SplitWordPattern.
        ExtractionNeedsReview.HasOcrArtifacts("Owl b ear").Should().BeTrue();

        var action = GroundingActionPolicy.Decide(Verdict(GroundingStatus.Grounded), "Owl b ear");

        action.Should().Be(GroundingAction.LeaveFlagged);
    }

    [Fact]
    public void Ungrounded_is_marked_ungrounded_regardless_of_name()
    {
        var action = GroundingActionPolicy.Decide(Verdict(GroundingStatus.Ungrounded), "Owlbear");

        action.Should().Be(GroundingAction.MarkUngrounded);
    }

    [Fact]
    public void Uncertain_is_left_flagged_regardless_of_name()
    {
        var action = GroundingActionPolicy.Decide(Verdict(GroundingStatus.Uncertain), "Owlbear");

        action.Should().Be(GroundingAction.LeaveFlagged);
    }
}