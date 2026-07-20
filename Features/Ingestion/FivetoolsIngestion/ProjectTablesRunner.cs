using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>Result of projecting one book's canonical tables from local 5etools data.</summary>
public sealed record ProjectResult(bool Skipped, string? SkipReason, int TableCount);

/// <summary>
/// Loads a book's canonical JSON, replaces its <c>tables[]</c> wholesale from the local 5etools
/// data (official books only — identified by a non-empty <see cref="CanonicalBookMetadata.SourceBook"/>),
/// writes it back, then reloads it as a round-trip proof (the loader throws on duplicate table ids).
/// Homebrew books (no source key) are left untouched.
/// </summary>
public static class ProjectTablesRunner
{
    public static async Task<ProjectResult> RunOneAsync(
        string canonicalPath, string fivetoolsDir,
        CanonicalJsonLoader loader, CanonicalJsonWriter writer, CancellationToken ct)
    {
        var file = await loader.LoadAsync(canonicalPath, ct);
        var key = file.Book.SourceBook;
        if (string.IsNullOrWhiteSpace(key))
            return new ProjectResult(Skipped: true, SkipReason: "no fivetoolsSourceKey (homebrew)", TableCount: 0);

        var tables = new FivetoolsTableProjection().BuildForBook(fivetoolsDir, key);
        await writer.WriteAsync(canonicalPath, file with { Tables = tables }, ct);

        // Round-trip proof: reload validates schema + unique ids; throws on any violation.
        await loader.LoadAsync(canonicalPath, ct);
        return new ProjectResult(Skipped: false, SkipReason: null, TableCount: tables.Count);
    }
}
