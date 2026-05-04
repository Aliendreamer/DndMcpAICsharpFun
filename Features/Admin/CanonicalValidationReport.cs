namespace DndMcpAICsharpFun.Features.Admin;

public sealed record CanonicalValidationFailure(string File, string Kind, string Detail);

public sealed record CanonicalValidationWarning(string File, string SourceEntityId, string FieldPath, string MissingTargetId);

public sealed record CanonicalValidationReport(
    int FilesScanned,
    int TotalEntities,
    IReadOnlyList<CanonicalValidationFailure> Failures,
    IReadOnlyList<CanonicalValidationWarning> Warnings);
