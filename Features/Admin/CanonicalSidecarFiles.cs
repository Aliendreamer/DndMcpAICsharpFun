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
    private static readonly string[] Suffixes =
    {
        ".errors.json",
        ".warnings.json",
        ".declined.json",
        ".progress.json",
        ".progress.errors.json",
    };

    /// <summary>True if <paramref name="path"/> is a sidecar file, not a canonical entity file.</summary>
    public static bool IsSidecar(string path) =>
        Suffixes.Any(s => path.EndsWith(s, StringComparison.Ordinal));
}