using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>
/// Result of a monster backfill diff for one book.  <see cref="ToAppend"/> holds the newly-built
/// Monster entities (gaps not present in the canonical); the caller appends them and writes the file.
/// The remaining fields form the monster-recall report: how many roster monsters are already present,
/// which are missing/extra, and how many existing monsters are grounded vs previously backfilled.
/// </summary>
public sealed record MonsterBackfillResult(
    bool HasSourceKey,
    string? CanonicalPath,
    IReadOnlyList<EntityEnvelope> ToAppend,
    int AlreadyPresent,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Extra,
    int GroundedCount,
    int BackfilledCount);

/// <summary>
/// Diffs a book's canonical Monster entities against the 5etools monsters of the book's source key.
/// Its <see cref="ComputeAsync"/> is the recall check (report only); applying <see cref="MonsterBackfillResult.ToAppend"/>
/// is the gap-only, idempotent backfill.  Mirrors <see cref="SpellBackfillService"/>.
/// </summary>
public sealed class MonsterBackfillService
{
    private const string BackfillDataSource = "5etools-backfill";

    private readonly BookSourceRegistry _books;
    private readonly CanonicalJsonLoader _loader;
    private readonly string _canonicalDirectory;
    private readonly string _fivetoolsDirectory;

    public MonsterBackfillService(
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
    /// Diffs the book's canonical Monster entities against the 5etools monsters of the book's source key,
    /// and returns a Monster entity for each 5etools monster whose normalized name is absent.
    /// A book with no <see cref="IngestionRecord.FivetoolsSourceKey"/> yields an empty no-op result.
    /// </summary>
    public async Task<MonsterBackfillResult> ComputeAsync(IngestionRecord record, CancellationToken ct)
    {
        var key = record.FivetoolsSourceKey;
        if (string.IsNullOrWhiteSpace(key))
            return new MonsterBackfillResult(
                false, null, Array.Empty<EntityEnvelope>(), 0,
                Array.Empty<string>(), Array.Empty<string>(), 0, 0);

        // Canonical slug derives from the source key (e.g. MM → mm) via the id-slug override table.
        var slug = EntityIdSlug.BookSlug(key);
        var canonicalPath = Path.Combine(_canonicalDirectory, slug + ".json");

        // Collect existing Monster entity normalized names (the gap diff key), keeping their display
        // names for the Extra report, and count grounded vs previously-backfilled monsters.
        var canonicalNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var grounded = 0;
        var backfilled = 0;
        if (File.Exists(canonicalPath))
        {
            var file = await _loader.LoadAsync(canonicalPath, ct);
            foreach (var e in file.Entities)
            {
                if (e.Type != EntityType.Monster) continue;
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
        var toAppend = new List<EntityEnvelope>();
        var missing = new List<string>();
        var alreadyPresent = 0;

        foreach (var monster in EnumerateFivetoolsMonsters(key))
        {
            ct.ThrowIfCancellationRequested();

            var name = monster.GetProperty("name").GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var norm = EntityNameIndex.Normalize(name);
            rosterNames.Add(norm);

            // seen.Add returns false when the name is already there (canonical or an earlier gap).
            if (!seen.Add(norm))
            {
                alreadyPresent++;
                continue;
            }

            missing.Add(name);
            toAppend.Add(BuildEntity(key, edition, name, monster));
        }

        // Extra: canonical monsters absent from the 5etools roster (report only; never deleted).
        var extra = canonicalNames
            .Where(kv => !rosterNames.Contains(kv.Key))
            .Select(kv => kv.Value)
            .ToList();

        return new MonsterBackfillResult(
            true, canonicalPath, toAppend, alreadyPresent, missing, extra, grounded, backfilled);
    }

    /// <summary>Raw 5etools monster records for <paramref name="sourceKey"/> (source-filtered).</summary>
    private IEnumerable<JsonElement> EnumerateFivetoolsMonsters(string sourceKey)
    {
        var bestiaryDir = Path.Combine(_fivetoolsDirectory, "bestiary");
        if (!Directory.Exists(bestiaryDir)) yield break;

        foreach (var path in Directory.GetFiles(bestiaryDir, "bestiary-*.json")
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
                if (!doc.RootElement.TryGetProperty("monster", out var arr)
                    || arr.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var el in arr.EnumerateArray())
                {
                    if (!el.TryGetProperty("source", out var src)
                        || src.ValueKind != JsonValueKind.String)
                        continue;
                    if (!string.Equals(src.GetString(), sourceKey, StringComparison.OrdinalIgnoreCase))
                        continue;

                    yield return el.Clone();
                }
            }
        }
    }

    private static EntityEnvelope BuildEntity(string key, string edition, string name, JsonElement monster)
    {
        int? page = monster.TryGetProperty("page", out var pg) && pg.TryGetInt32(out var pv) ? pv : null;
        var srd   = monster.TryGetProperty("srd",   out var s)  && s.ValueKind  == JsonValueKind.True;
        var srd52 = monster.TryGetProperty("srd52", out var s2) && s2.ValueKind == JsonValueKind.True;

        return new EntityEnvelope(
            Id: EntityIdSlug.For(key, EntityType.Monster, name),
            Type: EntityType.Monster,
            Name: name,
            SourceBook: key,
            Edition: edition,
            Page: page,
            FirstAppearedIn: new FirstAppearance(key, edition, page),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: "",
            Fields: BuildFields(monster),
            DataSource: BackfillDataSource,
            Srd: srd,
            Srd52: srd52,
            BasicRules2024: false,
            NeedsReview: false,
            Keywords: GetKeywords(monster),
            Disposition: EntityDisposition.Accepted);
    }

    /// <summary>Copies the traitTags strings as keywords (mirrors <c>FivetoolsMonsterMapper.GetKeywords</c>).</summary>
    private static IReadOnlyList<string> GetKeywords(JsonElement monster)
    {
        if (!monster.TryGetProperty("traitTags", out var tags) || tags.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return tags.EnumerateArray()
            .Where(t => t.ValueKind == JsonValueKind.String)
            .Select(t => t.GetString()!)
            .ToList();
    }

    /// <summary>
    /// The canonical Monster <c>fields</c> property names (see <c>Domain/Entities/Fields/MonsterFields.cs</c>);
    /// 5etools uses the identical property names, so the projection is a 1:1 copy of the present ones.
    /// </summary>
    private static readonly string[] MonsterFieldNames =
    {
        "size", "type", "alignment", "ac", "hp", "speed",
        "str", "dex", "con", "int", "wis", "cha",
        "save", "skill", "resist", "immune", "vulnerable", "conditionImmune",
        "senses", "passive", "languages", "cr",
        "trait", "action", "bonus", "reaction", "legendary", "legendaryHeader",
        "lair", "lairHeader", "spellcasting", "environment",
    };

    /// <summary>
    /// Builds the canonical Monster <c>fields</c> shape by copying each present monster stat-block
    /// property from the 5etools element.  Deserialises 1:1 as a <see cref="Domain.Entities.Fields.MonsterFields"/>.
    /// </summary>
    private static JsonElement BuildFields(JsonElement monster)
    {
        var fields = new JsonObject();
        foreach (var prop in MonsterFieldNames)
        {
            var node = CopyOrNull(monster, prop);
            if (node is not null)
                fields[prop] = node;
        }

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }

    private static JsonNode? CopyOrNull(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null
            ? JsonNode.Parse(v.GetRawText())
            : null;
}
