namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public enum GroundingAction { Promote, MarkUngrounded, LeaveFlagged }

public static class GroundingActionPolicy
{
    public static GroundingAction Decide(GroundingVerdict verdict, string name) =>
        verdict.Status switch
        {
            GroundingStatus.Grounded => ExtractionNeedsReview.HasOcrArtifacts(name)
                ? GroundingAction.LeaveFlagged
                : GroundingAction.Promote,
            GroundingStatus.Ungrounded => GroundingAction.MarkUngrounded,
            GroundingStatus.Uncertain => GroundingAction.LeaveFlagged,
            _ => GroundingAction.LeaveFlagged,
        };
}
