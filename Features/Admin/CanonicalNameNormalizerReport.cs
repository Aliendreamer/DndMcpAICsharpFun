namespace DndMcpAICsharpFun.Features.Admin;

public sealed record CanonicalNameNormalizerFileResult(
    string File,
    int TitleCased,
    int Flagged,
    int Unchanged);

public sealed record CanonicalNameNormalizerReport(
    int FilesScanned,
    int TotalEntities,
    bool DryRun,
    IReadOnlyList<CanonicalNameNormalizerFileResult> Changes);
