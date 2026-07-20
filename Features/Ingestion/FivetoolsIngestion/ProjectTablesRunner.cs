using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>Result of projecting one book's canonical tables from local 5etools data.</summary>
public sealed record ProjectResult(bool Skipped, string? SkipReason, int TableCount);

/// <summary>
/// Loads a book's canonical JSON, replaces its <c>tables[]</c> wholesale from the local 5etools
/// data (official books only — identified by a non-empty <see cref="CanonicalBookMetadata.SourceBook"/>),
/// composing the generic 5etools table projection with the normalized resolution artifacts from
/// <see cref="DraconicAncestryResolutionProjector"/> (which cedes its owned table ids from the generic
/// projection and authors the corresponding <c>choiceSets[]</c>), writes it back, then reloads it as a
/// round-trip proof (the loader throws on duplicate table ids or unresolved choiceset table references).
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

        var generic = new FivetoolsTableProjection().BuildForBook(fivetoolsDir, key);
        var resolution = DraconicAncestryResolutionProjector.Project(fivetoolsDir, key);

        // Resolution owns its table ids (e.g. phb14.table.draconic-ancestry in normalized shape) — drop the
        // generic same-id tables so the resolver's expected shape wins.
        var ownedIds = resolution.Tables.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var subclassSpells = SubclassSpellsProjector.Project(fivetoolsDir, key);
        var tables = generic.Where(t => !ownedIds.Contains(t.Id))
            .Concat(resolution.Tables)
            .Concat(subclassSpells)
            .ToList();

        // Never wipe a book's tables to empty: if we have nothing to offer (e.g. a monster/reference
        // book with no scannable 5etools captioned tables and no classes), leave the canonical untouched.
        if (tables.Count == 0)
            return new ProjectResult(Skipped: true, SkipReason: "no projectable tables", TableCount: 0);

        // Author resolution choiceSets when present; otherwise keep any existing choiceSets untouched.
        var choiceSets = resolution.ChoiceSets.Count > 0 ? resolution.ChoiceSets : file.ChoiceSets;

        await writer.WriteAsync(canonicalPath, file with { Tables = tables, ChoiceSets = choiceSets }, ct);

        // Round-trip proof: reload validates schema + unique ids + choiceset->table references; throws on any violation.
        await loader.LoadAsync(canonicalPath, ct);
        return new ProjectResult(Skipped: false, SkipReason: null, TableCount: tables.Count);
    }
}