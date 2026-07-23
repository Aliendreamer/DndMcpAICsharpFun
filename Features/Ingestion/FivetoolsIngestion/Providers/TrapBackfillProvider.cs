using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.Trap"/>. Reads
/// <c>trapshazards.json</c>'s <c>"trap"</c> array ONLY (the sibling <c>"hazard"</c> array has no
/// modeled <see cref="EntityType"/> and is out of scope here — it is surfaced only via the
/// coverage service's unmodeled bucket) and projects a curated
/// <see cref="Domain.Entities.Fields.TrapFields"/> shape — self-contained, like
/// <see cref="GodBackfillProvider"/>/<see cref="SpellBackfillProvider"/>, NOT the generic
/// field-fill mapper's raw clone.
/// </summary>
public sealed class TrapBackfillProvider : IFivetoolsBackfillProvider
{
    public EntityType Type => EntityType.Trap;

    /// <summary>Raw 5etools trap records from trapshazards.json's "trap" array (no filter).</summary>
    public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
    {
        var path = Path.Combine(fivetoolsDir, "trapshazards.json");
        if (!File.Exists(path)) yield break;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllBytes(path)); }
        catch (JsonException) { yield break; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("trap", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                yield break;

            foreach (var el in arr.EnumerateArray())
                yield return el.Clone();
        }
    }

    public EntityEnvelope BuildEntity(string sourceKey, string edition, string name, JsonElement element)
    {
        int? page = element.TryGetProperty("page", out var pg) && pg.TryGetInt32(out var pv) ? pv : null;
        var srd = element.TryGetProperty("srd", out var s) && s.ValueKind == JsonValueKind.True;
        var srd52 = element.TryGetProperty("srd52", out var s2) && s2.ValueKind == JsonValueKind.True;

        return new EntityEnvelope(
            Id: EntityIdSlug.For(sourceKey, EntityType.Trap, name),
            Type: EntityType.Trap,
            Name: name,
            SourceBook: sourceKey,
            Edition: edition,
            Page: page,
            FirstAppearedIn: new FirstAppearance(sourceKey, edition, page),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: "",
            Fields: BuildFields(element),
            DataSource: "5etools-backfill",
            Srd: srd,
            Srd52: srd52,
            BasicRules2024: false,
            NeedsReview: false,
            Keywords: Array.Empty<string>(),
            Disposition: EntityDisposition.Accepted);
    }

    /// <summary>
    /// Builds the canonical Trap <c>fields</c> shape (see
    /// <see cref="Domain.Entities.Fields.TrapFields"/>): a difficulty label from the first
    /// <c>rating[].threat</c> entry (current 5etools traps carry a tier/threat rating rather than
    /// explicit DCs), the top-level <c>detectDc</c>/<c>disarmDc</c> numbers when present (older
    /// simple-trap shape), and a description assembled from <c>entries[]</c>.
    /// </summary>
    private static JsonElement BuildFields(JsonElement trap)
    {
        var fields = new JsonObject
        {
            ["difficulty"] = GetDifficulty(trap),
            ["detectDc"] = CopyIntOrNull(trap, "detectDc"),
            ["disarmDc"] = CopyIntOrNull(trap, "disarmDc"),
            ["description"] = GetDescription(trap),
        };

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }

    private static string GetDifficulty(JsonElement trap)
    {
        if (trap.TryGetProperty("rating", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
        {
            var first = arr[0];
            if (first.TryGetProperty("threat", out var t) && t.ValueKind == JsonValueKind.String)
                return Titleize(t.GetString()!);
        }
        return "";
    }

    private static JsonNode? CopyIntOrNull(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? JsonValue.Create(v.GetInt32())
            : null;

    private static string GetDescription(JsonElement trap)
    {
        if (!trap.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return "";
        return FivetoolsEntryText.Flatten(entries);
    }

    private static string Titleize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}