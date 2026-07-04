using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>
/// Result of an entity backfill diff for one book.  <see cref="ToAppend"/> holds the newly-built
/// entities (gaps not present in the canonical); the caller appends them and writes the file.
/// The remaining fields form the recall report: how many roster elements are already present,
/// which are missing/extra, and how many existing entities are grounded vs previously backfilled.
/// </summary>
public sealed record EntityBackfillResult(
    bool HasSourceKey,
    string? CanonicalPath,
    IReadOnlyList<EntityEnvelope> ToAppend,
    int AlreadyPresent,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Extra,
    int GroundedCount,
    int BackfilledCount,
    IReadOnlyList<string> ExtraOtherSource,
    IReadOnlyList<string> ExtraUnknown);

/// <summary>
/// Result of the precision-flag operation: which canonical entities (from <see cref="EntityBackfillResult.ExtraUnknown"/>)
/// had <c>NeedsReview</c> newly set true and the canonical rewritten.  Never deletes an entity and never
/// touches <see cref="EntityBackfillResult.ExtraOtherSource"/> entities.
/// </summary>
public sealed record EntityFlagResult(
    bool HasSourceKey,
    string? CanonicalPath,
    IReadOnlyList<string> Flagged);

/// <summary>
/// Diffs a book's canonical entities of a given <see cref="IFivetoolsBackfillProvider.Type"/> against the
/// 5etools roster for that type and the book's source key. Its <see cref="ComputeAsync"/> is the recall
/// check (report only); applying <see cref="EntityBackfillResult.ToAppend"/> is the gap-only, idempotent
/// backfill. Generalises the per-type <c>MonsterBackfillService</c>/<c>SpellBackfillService</c> via the
/// <see cref="IFivetoolsBackfillProvider"/> seam so one engine serves every backfillable entity type.
/// </summary>
public sealed class EntityBackfillService
{
    private const string BackfillDataSource = "5etools-backfill";

    private readonly IFivetoolsBackfillProvider _provider;
    private readonly BookSourceRegistry _books;
    private readonly CanonicalJsonLoader _loader;
    private readonly string _canonicalDirectory;
    private readonly string _fivetoolsDirectory;

    public EntityBackfillService(
        IFivetoolsBackfillProvider provider,
        BookSourceRegistry books,
        CanonicalJsonLoader loader,
        string canonicalDirectory,
        string fivetoolsDirectory)
    {
        _provider = provider;
        _books = books;
        _loader = loader;
        _canonicalDirectory = canonicalDirectory;
        _fivetoolsDirectory = fivetoolsDirectory;
    }

    /// <summary>
    /// Diffs the book's canonical entities of <see cref="IFivetoolsBackfillProvider.Type"/> against the
    /// 5etools roster of the book's source key, and returns an entity for each roster element whose
    /// normalized name is absent. A book with no <see cref="IngestionRecord.FivetoolsSourceKey"/> yields
    /// an empty no-op result.
    /// </summary>
    public async Task<EntityBackfillResult> ComputeAsync(IngestionRecord record, CancellationToken ct)
    {
        var key = record.FivetoolsSourceKey;
        if (string.IsNullOrWhiteSpace(key))
            return new EntityBackfillResult(
                false, null, Array.Empty<EntityEnvelope>(), 0,
                Array.Empty<string>(), Array.Empty<string>(), 0, 0,
                Array.Empty<string>(), Array.Empty<string>());

        // Canonical slug derives from the source key (e.g. MM → mm) via the id-slug override table.
        var slug = EntityIdSlug.BookSlug(key);
        var canonicalPath = Path.Combine(_canonicalDirectory, slug + ".json");

        // Collect existing entity normalized names (the gap diff key), keeping their display
        // names for the Extra report, and count grounded vs previously-backfilled entities.
        var canonicalNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var grounded = 0;
        var backfilled = 0;
        if (File.Exists(canonicalPath))
        {
            var file = await _loader.LoadAsync(canonicalPath, ct);
            foreach (var e in file.Entities)
            {
                if (e.Type != _provider.Type) continue;
                canonicalNames[EntityNameIndex.Normalize(e.Name)] = e.Name;
                if (string.Equals(e.DataSource, BackfillDataSource, StringComparison.Ordinal))
                    backfilled++;
                else
                    grounded++;
            }
        }

        var info = _books.TryGetBook(key);
        var edition = ((info is not null && info.PublishedYear >= 2024) || FivetoolsMapperBase.Edition2024Sources.Contains(key))
            ? "Edition2024"
            : "Edition2014";

        // Working set seeds with the canonical names so a name present there (or an earlier gap) is not re-added.
        var seen = new HashSet<string>(canonicalNames.Keys, StringComparer.Ordinal);
        var rosterNames = new HashSet<string>(StringComparer.Ordinal);
        // ALL-source element names (every roster element of this type, any source) — used to split Extra
        // below into cross-printed-elsewhere (extraOtherSource) vs matching no roster element at all (extraUnknown).
        var allSourceNames = new HashSet<string>(StringComparer.Ordinal);
        var toAppend = new List<EntityEnvelope>();
        var missing = new List<string>();
        var alreadyPresent = 0;

        foreach (var element in _provider.EnumerateRoster(_fivetoolsDirectory))
        {
            ct.ThrowIfCancellationRequested();

            var name = element.GetProperty("name").GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var norm = EntityNameIndex.Normalize(name);
            allSourceNames.Add(norm);

            var source = element.TryGetProperty("source", out var srcProp) && srcProp.ValueKind == JsonValueKind.String
                ? srcProp.GetString()
                : null;
            if (!string.Equals(source, key, StringComparison.OrdinalIgnoreCase))
                continue;

            rosterNames.Add(norm);

            // seen.Add returns false when the name is already there (canonical or an earlier gap).
            if (!seen.Add(norm))
            {
                alreadyPresent++;
                continue;
            }

            missing.Add(name);
            toAppend.Add(_provider.BuildEntity(key, edition, name, element));
        }

        // Extra: canonical entities absent from the 5etools roster (report only; never deleted).  Split
        // into extraOtherSource (name matches an element of this type in the full 5etools index, just
        // under a different source — plausibly real, a cross-print) and extraUnknown (matches no roster
        // element at all — dominated by OCR garbage / cross-book contamination).
        var extraEntries = canonicalNames
            .Where(kv => !rosterNames.Contains(kv.Key))
            .ToList();
        var extra = extraEntries.Select(kv => kv.Value).ToList();
        var extraOtherSource = extraEntries.Where(kv => allSourceNames.Contains(kv.Key)).Select(kv => kv.Value).ToList();
        var extraUnknown = extraEntries.Where(kv => !allSourceNames.Contains(kv.Key)).Select(kv => kv.Value).ToList();

        return new EntityBackfillResult(
            true, canonicalPath, toAppend, alreadyPresent, missing, extra, grounded, backfilled,
            extraOtherSource, extraUnknown);
    }

    /// <summary>
    /// Precision-flag operation: computes <see cref="EntityBackfillResult.ExtraUnknown"/> and sets
    /// <c>NeedsReview = true</c> on each matching canonical entity, rewriting the canonical via
    /// <paramref name="writer"/> (the same write path as the backfill apply).  Gap-only: an entity whose
    /// <c>NeedsReview</c> is already true is left alone (not re-flagged, not re-written unnecessarily).
    /// Never deletes an entity and never touches <see cref="EntityBackfillResult.ExtraOtherSource"/> entities.
    /// </summary>
    public async Task<EntityFlagResult> FlagUnknownAsync(IngestionRecord record, CanonicalJsonWriter writer, CancellationToken ct)
    {
        var result = await ComputeAsync(record, ct);
        if (!result.HasSourceKey)
            return new EntityFlagResult(false, null, Array.Empty<string>());

        if (result.CanonicalPath is null || !File.Exists(result.CanonicalPath))
            return new EntityFlagResult(true, result.CanonicalPath, Array.Empty<string>());

        var unknown = new HashSet<string>(result.ExtraUnknown, StringComparer.Ordinal);
        if (unknown.Count == 0)
            return new EntityFlagResult(true, result.CanonicalPath, Array.Empty<string>());

        var file = await _loader.LoadAsync(result.CanonicalPath, ct);
        var flagged = new List<string>();
        var entities = new List<EntityEnvelope>(file.Entities.Count);
        foreach (var e in file.Entities)
        {
            if (e.Type == _provider.Type && !e.NeedsReview && unknown.Contains(e.Name))
            {
                flagged.Add(e.Name);
                entities.Add(e with { NeedsReview = true });
            }
            else
            {
                entities.Add(e);
            }
        }

        if (flagged.Count > 0)
            await writer.WriteAsync(result.CanonicalPath, file with { Entities = entities }, ct);

        return new EntityFlagResult(true, result.CanonicalPath, flagged);
    }
}
