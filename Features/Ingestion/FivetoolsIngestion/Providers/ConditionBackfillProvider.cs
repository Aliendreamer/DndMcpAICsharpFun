using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.Condition"/>. Reads
/// <c>conditionsdiseases.json</c>'s <c>"condition"</c> array ONLY (the sibling <c>"disease"</c>
/// array is out of scope for this type) and projects a curated
/// <see cref="Domain.Entities.Fields.ConditionFields"/> shape — self-contained, like
/// <see cref="GodBackfillProvider"/>/<see cref="SpellBackfillProvider"/>, NOT the generic
/// field-fill mapper's raw clone.
/// </summary>
public sealed class ConditionBackfillProvider : IFivetoolsBackfillProvider
{
    public EntityType Type => EntityType.Condition;

    /// <summary>Raw 5etools condition records from conditionsdiseases.json's "condition" array (no filter).</summary>
    public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
    {
        var path = Path.Combine(fivetoolsDir, "conditionsdiseases.json");
        if (!File.Exists(path)) yield break;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllBytes(path)); }
        catch (JsonException) { yield break; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("condition", out var arr)
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
            Id: EntityIdSlug.For(sourceKey, EntityType.Condition, name),
            Type: EntityType.Condition,
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
    /// Builds the canonical Condition <c>fields</c> shape (see
    /// <see cref="Domain.Entities.Fields.ConditionFields"/>): a single description assembled from
    /// the string/nested-block entries of <c>entries[]</c>.
    /// </summary>
    private static JsonElement BuildFields(JsonElement condition)
    {
        var fields = new JsonObject
        {
            ["description"] = GetDescription(condition),
        };

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }

    private static string GetDescription(JsonElement condition)
    {
        if (!condition.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return "";
        return FivetoolsEntryText.Flatten(entries);
    }
}