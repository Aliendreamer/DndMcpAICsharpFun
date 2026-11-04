namespace DndMcpAICsharpFun.Features.Admin;

/// <summary>
/// Sidecar JSON files written alongside a book's canonical <c>&lt;slug&gt;.json</c>
/// (errors / warnings / declined records and run checkpoints). They are NOT
/// <see cref="DndMcpAICsharpFun.Domain.Entities.CanonicalJsonFile"/> documents, so any
/// enumeration of canonical files for validation or normalization MUST skip them — otherwise
/// the loader fails trying to parse a sidecar (e.g. <c>&lt;slug&gt;.declined.json</c>) as a
/// canonical file.
/// </summary>
public static class CanonicalSidecarFiles
{
    // Sidecars of the LLM extraction pipeline: stale once a book is re-extracted programmatically.
    private static readonly string[] ReplaceableSuffixes =
    {
        ".errors.json",
        ".warnings.json",
        ".declined.json",
        ".progress.json",
        ".progress.errors.json",
    };

    // Programmatic-extraction sidecars: not canonical documents either, but they survive re-extraction
    // (the overrides file holds hand corrections) or are rewritten by it (the report).
    private static readonly string[] Suffixes =
    [
        .. ReplaceableSuffixes,
        ".report.json",
        ".overrides.json",
    ];

    /// <summary>Paths of the LLM-pipeline sidecars for <paramref name="slug"/> that exist in <paramref name="dir"/>.</summary>
    public static IEnumerable<string> ReplaceableSidecarPaths(string dir, string slug) =>
        ReplaceableSuffixes.Select(s => Path.Combine(dir, slug + s)).Where(File.Exists);

    /// <summary>True if <paramref name="path"/> is a sidecar file, not a canonical entity file.</summary>
    public static bool IsSidecar(string path) =>
        Suffixes.Any(s => path.EndsWith(s, StringComparison.Ordinal));
}