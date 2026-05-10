namespace DndMcpAICsharpFun.Features.Admin;

public sealed record CanonicalValidationFailure(string File, string Kind, string Detail);

public sealed record CanonicalValidationWarning(string File, string SourceEntityId, string FieldPath, string MissingTargetId);

public sealed record CanonicalNeedsReviewWarning(string File, int Count);

public sealed record CanonicalValidationReport(
    int FilesScanned,
    int TotalEntities,
    IReadOnlyList<CanonicalValidationFailure> Failures,
    IReadOnlyList<CanonicalValidationWarning> Warnings,
    IReadOnlyList<CanonicalNeedsReviewWarning> NeedsReview = null!)
{
    public IReadOnlyList<CanonicalNeedsReviewWarning> NeedsReview { get; init; } = NeedsReview ?? [];
}
