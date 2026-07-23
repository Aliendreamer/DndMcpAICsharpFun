namespace DndMcpAICsharpFun.Features.Admin;

public sealed record CanonicalValidationFailure(string File, string Kind, string Detail);

public sealed record CanonicalValidationWarning(string File, string SourceEntityId, string FieldPath, string MissingTargetId);

public sealed record CanonicalNeedsReviewWarning(string File, int Count);

/// <summary>
/// Non-blocking 5etools coverage summary for one canonical file (Task 6, surface b): a book below
/// full coverage against its 5etools source is reported here — never as a <see cref="CanonicalValidationFailure"/>,
/// so it can never turn <c>POST /admin/canonical/validate</c> into a 422.
/// </summary>
public sealed record CanonicalCoverageWarning(string File, string SourceKey, double CoveragePct, int TotalMissing);

public sealed record CanonicalValidationReport(
    int FilesScanned,
    int TotalEntities,
    IReadOnlyList<CanonicalValidationFailure> Failures,
    IReadOnlyList<CanonicalValidationWarning> Warnings,
    IReadOnlyList<CanonicalNeedsReviewWarning> NeedsReview = null!,
    IReadOnlyList<CanonicalCoverageWarning> Coverage = null!)
{
    public IReadOnlyList<CanonicalNeedsReviewWarning> NeedsReview { get; init; } = NeedsReview ?? [];
    public IReadOnlyList<CanonicalCoverageWarning> Coverage { get; init; } = Coverage ?? [];
}