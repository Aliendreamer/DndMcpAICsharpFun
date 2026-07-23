using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>
/// Per-type gap report for one modeled catalog type within a book: how many 5etools roster
/// elements (of the book's source key) exist, how many are matched by name in the canonical
/// (<see cref="Present"/>), and the NAMED gaps (<see cref="MissingNames"/>). <see cref="Present"/>
/// counts only roster-matched canonical entities — it deliberately excludes the "Extra" canonical
/// entries that <see cref="EntityBackfillService"/> tracks separately (OCR garbage / cross-book
/// contamination not matched by any current roster element), so <see cref="RosterCount"/> and
/// <see cref="Present"/> stay in the same universe and a type can never read as more than 100% covered.
/// </summary>
public sealed record TypeCoverage(
    EntityType Type,
    int RosterCount,
    int Present,
    int MissingCount,
    IReadOnlyList<string> MissingNames);

/// <summary>
/// Coverage-against-5etools report for one official book: the per-type gap breakdown
/// (<see cref="PerType"/>), a small bucket of 5etools content types the book has entries for but no
/// <see cref="EntityType"/> models yet (<see cref="Unmodeled"/> — e.g. optionalfeatures), and the
/// aggregate totals/percentage. A book with no <see cref="IngestionRecord.FivetoolsSourceKey"/> (not
/// an official 5etools source) yields <see cref="Empty"/>.
/// </summary>
public sealed record BookCoverage(
    string SourceKey,
    IReadOnlyList<TypeCoverage> PerType,
    IReadOnlyList<string> Unmodeled,
    int TotalPresent,
    int TotalRoster,
    double CoveragePct)
{
    public static readonly BookCoverage Empty =
        new(string.Empty, Array.Empty<TypeCoverage>(), Array.Empty<string>(), 0, 0, 0);
}

/// <summary>
/// Aggregates <see cref="EntityBackfillService.ComputeAsync"/> across every registered provider for
/// one book, WITHOUT applying any backfill — a read-only gap report (report + warn surfaces consume
/// this; nothing here ever writes canonical JSON). Also flags 5etools content the book has for a
/// handful of catalog files that map to no <see cref="EntityType"/> at all (see
/// <see cref="UnmodeledCatalog"/>), so a gap like the PHB optionalfeatures the backfill engine cannot
/// yet touch (no <c>OptionalFeature</c> EntityType exists) stays visible instead of silently
/// vanishing from the coverage picture.
/// </summary>
public sealed class FivetoolsCoverageService(
    IReadOnlyDictionary<EntityType, EntityBackfillService> services,
    string fivetoolsDirectory)
{
    /// <summary>
    /// Small, hand-maintained list of 5etools catalog files with NO corresponding <see cref="EntityType"/>
    /// at all — i.e. content no provider could ever claim, as opposed to a modeled type that simply
    /// has no provider yet (Class/Subclass/Race/Subrace/etc., which DO have an EntityType and are out
    /// of scope for this bucket — they are measured-only, not "unmodeled"). Each entry is (label, file
    /// name under the 5etools data directory, top-level array property). Keep this tiny and review it
    /// alongside <see cref="EntityType"/> changes: if a future EntityType covers one of these (e.g. an
    /// OptionalFeature type), remove the corresponding entry here.
    /// </summary>
    private static readonly (string Label, string FileName, string ArrayKey)[] UnmodeledCatalog =
    [
        ("optionalfeatures", "optionalfeatures.json", "optionalfeature"),
        ("senses", "senses.json", "sense"),
        ("psionics", "psionics.json", "psionic"),
        ("rewards", "rewards.json", "reward"),
    ];

    /// <summary>
    /// Computes the coverage report for <paramref name="record"/> by running every registered
    /// provider's <see cref="EntityBackfillService.ComputeAsync"/> (never applying the result), then
    /// aggregating. A non-official book (no <see cref="IngestionRecord.FivetoolsSourceKey"/>) is a
    /// no-op returning <see cref="BookCoverage.Empty"/>.
    /// </summary>
    public async Task<BookCoverage> ComputeAsync(IngestionRecord record, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(record.FivetoolsSourceKey))
            return BookCoverage.Empty;

        var key = record.FivetoolsSourceKey;
        var perType = new List<TypeCoverage>();
        var totalPresent = 0;
        var totalRoster = 0;

        foreach (var (type, service) in services.OrderBy(static kv => (int)kv.Key))
        {
            ct.ThrowIfCancellationRequested();
            var result = await service.ComputeAsync(record, ct);
            if (!result.HasSourceKey)
                continue;

            var present = result.AlreadyPresent;
            var missingCount = result.Missing.Count;
            var roster = present + missingCount;

            perType.Add(new TypeCoverage(type, roster, present, missingCount, result.Missing));
            totalPresent += present;
            totalRoster += roster;
        }

        var unmodeled = FindUnmodeled(fivetoolsDirectory, key);
        // Vacuously "fully covered" when there is nothing to cover (no roster elements across any
        // registered provider) — never a NaN/negative, and never a value the reader could mistake for
        // a genuine 100%-of-something-real without also checking TotalRoster.
        var coveragePct = totalRoster == 0 ? 100.0 : Math.Round(100.0 * totalPresent / totalRoster, 1);

        return new BookCoverage(key, perType, unmodeled, totalPresent, totalRoster, coveragePct);
    }

    /// <summary>Returns the <see cref="UnmodeledCatalog"/> labels for which the 5etools data actually
    /// has at least one element sourced to <paramref name="key"/> — reported only when relevant to
    /// this book, never unconditionally.</summary>
    private static List<string> FindUnmodeled(string fivetoolsDirectory, string key)
    {
        var found = new List<string>();
        foreach (var (label, fileName, arrayKey) in UnmodeledCatalog)
        {
            var path = Path.Combine(fivetoolsDirectory, fileName);
            if (!File.Exists(path))
                continue;

            try
            {
                using var stream = File.OpenRead(path);
                using var doc = JsonDocument.Parse(stream);
                if (!doc.RootElement.TryGetProperty(arrayKey, out var array) || array.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var element in array.EnumerateArray())
                {
                    if (element.TryGetProperty("source", out var src)
                        && src.ValueKind == JsonValueKind.String
                        && string.Equals(src.GetString(), key, StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(label);
                        break;
                    }
                }
            }
            catch (JsonException)
            {
                // Malformed/unexpected catalog file — this bucket is a warning aid, never load-bearing.
            }
        }

        return found;
    }
}