using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>
/// Result of a spell backfill diff for one book.  <see cref="ToAppend"/> holds the newly-built
/// Spell entities (gaps not present in the canonical); the caller appends them and writes the file.
/// </summary>
public sealed record SpellBackfillResult(
    bool HasSourceKey,
    string? CanonicalPath,
    IReadOnlyList<EntityEnvelope> ToAppend,
    int AlreadyPresent);

/// <summary>
/// The B fallback of the hybrid push: a deterministic completeness pass that fills an official
/// book's missing spells from the authoritative 5etools spell data.  No LLM, no re-extraction.
/// The parsed canonical is the source of truth; backfill only fills verified gaps (by normalized
/// name) and marks them <c>dataSource:"5etools-backfill"</c>.
/// </summary>
public sealed class SpellBackfillService
{
    private readonly BookSourceRegistry _books;
    private readonly CanonicalJsonLoader _loader;
    private readonly string _canonicalDirectory;
    private readonly string _fivetoolsDirectory;

    public SpellBackfillService(
        BookSourceRegistry books,
        CanonicalJsonLoader loader,
        string canonicalDirectory,
        string fivetoolsDirectory)
    {
        _books = books;
        _loader = loader;
        _canonicalDirectory = canonicalDirectory;
        _fivetoolsDirectory = fivetoolsDirectory;
    }

    /// <summary>
    /// Diffs the book's canonical Spell entities against the 5etools spells of the book's source key,
    /// and returns a Spell entity for each 5etools spell whose normalized name is absent.
    /// A book with no <see cref="IngestionRecord.FivetoolsSourceKey"/> yields an empty no-op result.
    /// </summary>
    public async Task<SpellBackfillResult> ComputeAsync(IngestionRecord record, CancellationToken ct)
    {
        var key = record.FivetoolsSourceKey;
        if (string.IsNullOrWhiteSpace(key))
            return new SpellBackfillResult(false, null, Array.Empty<EntityEnvelope>(), 0);

        // Canonical slug derives from the source key (e.g. PHB → phb14) via the id-slug override table.
        var slug = EntityIdSlug.BookSlug(key);
        var canonicalPath = Path.Combine(_canonicalDirectory, slug + ".json");

        // Collect existing Spell entity normalized names (the gap diff key).
        var present = new HashSet<string>(StringComparer.Ordinal);
        if (File.Exists(canonicalPath))
        {
            var file = await _loader.LoadAsync(canonicalPath, ct);
            foreach (var e in file.Entities)
                if (e.Type == EntityType.Spell)
                    present.Add(EntityNameIndex.Normalize(e.Name));
        }

        var info = _books.TryGetBook(key);
        var edition = ((info is not null && info.PublishedYear >= 2024) || FivetoolsMapperBase.Edition2024Sources.Contains(key))
            ? "Edition2024"
            : "Edition2014";

        var toAppend = new List<EntityEnvelope>();
        var alreadyPresent = 0;

        foreach (var spell in EnumerateFivetoolsSpells(key))
        {
            ct.ThrowIfCancellationRequested();

            var name = spell.GetProperty("name").GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var norm = EntityNameIndex.Normalize(name);
            // present.Add returns false when the name is already there (canonical or an earlier gap).
            if (!present.Add(norm))
            {
                alreadyPresent++;
                continue;
            }

            toAppend.Add(BuildEntity(key, edition, name, spell));
        }

        return new SpellBackfillResult(true, canonicalPath, toAppend, alreadyPresent);
    }

    /// <summary>Raw 5etools spell records for <paramref name="sourceKey"/> (source-filtered, XPHB excluded when key is PHB).</summary>
    private IEnumerable<JsonElement> EnumerateFivetoolsSpells(string sourceKey)
    {
        var spellDir = Path.Combine(_fivetoolsDirectory, "spells");
        if (!Directory.Exists(spellDir)) yield break;

        foreach (var path in Directory.GetFiles(spellDir, "spells-*.json")
                     .Where(f =>
                     {
                         var n = Path.GetFileName(f);
                         return !n.StartsWith("fluff-", StringComparison.Ordinal)
                                && !n.Contains("index", StringComparison.Ordinal)
                                && !n.Contains("foundry", StringComparison.Ordinal);
                     })
                     .Order(StringComparer.Ordinal))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(File.ReadAllBytes(path)); }
            catch (JsonException) { continue; }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("spell", out var arr)
                    || arr.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var el in arr.EnumerateArray())
                {
                    if (!el.TryGetProperty("source", out var src)
                        || src.ValueKind != JsonValueKind.String)
                        continue;
                    // Strict source match: PHB (2014) spells only, never XPHB (2024).
                    if (!string.Equals(src.GetString(), sourceKey, StringComparison.OrdinalIgnoreCase))
                        continue;

                    yield return el.Clone();
                }
            }
        }
    }

    private static EntityEnvelope BuildEntity(string key, string edition, string name, JsonElement spell)
    {
        int? page = spell.TryGetProperty("page", out var pg) && pg.TryGetInt32(out var pv) ? pv : null;
        var srd   = spell.TryGetProperty("srd",   out var s)  && s.ValueKind  == JsonValueKind.True;
        var srd52 = spell.TryGetProperty("srd52", out var s2) && s2.ValueKind == JsonValueKind.True;

        return new EntityEnvelope(
            Id: EntityIdSlug.For(key, EntityType.Spell, name),
            Type: EntityType.Spell,
            Name: name,
            SourceBook: key,
            Edition: edition,
            Page: page,
            FirstAppearedIn: new FirstAppearance(key, edition, page),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: "",
            Fields: BuildFields(spell),
            DataSource: "5etools-backfill",
            Srd: srd,
            Srd52: srd52,
            BasicRules2024: false,
            NeedsReview: false,
            Keywords: Array.Empty<string>(),
            Disposition: EntityDisposition.Accepted);
    }

    /// <summary>
    /// Builds the canonical Spell <c>fields</c> shape: a single "Description" block wrapping the
    /// 5etools <c>entries</c>, plus <c>entriesHigherLevel</c> (when present) and
    /// <c>damageInflict</c>/<c>conditionInflict</c> (copied, else null) — mirroring parsed spells.
    /// </summary>
    private static JsonElement BuildFields(JsonElement spell)
    {
        var description = new JsonObject
        {
            ["type"] = "entries",
            ["name"] = "Description",
            ["entries"] = CopyOrEmptyArray(spell, "entries"),
        };

        var fields = new JsonObject { ["entries"] = new JsonArray(description) };

        if (spell.TryGetProperty("entriesHigherLevel", out var ehl) && ehl.ValueKind == JsonValueKind.Array)
            fields["entriesHigherLevel"] = JsonNode.Parse(ehl.GetRawText());

        fields["damageInflict"] = CopyOrNull(spell, "damageInflict");
        fields["conditionInflict"] = CopyOrNull(spell, "conditionInflict");

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }

    private static JsonNode CopyOrEmptyArray(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array
            ? JsonNode.Parse(v.GetRawText())!
            : new JsonArray();

    private static JsonNode? CopyOrNull(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null
            ? JsonNode.Parse(v.GetRawText())
            : null;
}
