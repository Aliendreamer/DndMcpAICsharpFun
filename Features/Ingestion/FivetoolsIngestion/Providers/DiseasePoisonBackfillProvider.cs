using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.DiseasePoison"/>. Reads
/// <c>conditionsdiseases.json</c>'s <c>"disease"</c> array ONLY — the only 5etools source that
/// maps to this type. 5etools has no separate poisons file/array; poison items live in
/// <c>items.json</c> as mundane gear (type <c>"G"</c>, rarity absent, tagged with
/// <c>poisonTypes</c>) and are backfilled as <see cref="EntityType.Item"/> instead (see
/// <see cref="ItemBackfillProvider"/>) — matching the existing
/// <see cref="FivetoolsSourceRegistry"/>'s registration of this type
/// (<c>AddGlobal("conditionsdiseases.json", EntityType.DiseasePoison, "disease")</c>, no
/// registration for a "poison" array). Projects the RAW <c>fields</c> shape the
/// <see cref="Features.Entities.CanonicalText.DiseasePoisonCanonicalTextRenderer"/> reads
/// (<c>entries</c> only) — NOT a curated domain-record shape.
/// </summary>
public sealed class DiseasePoisonBackfillProvider : IFivetoolsBackfillProvider
{
    public EntityType Type => EntityType.DiseasePoison;

    /// <summary>Raw 5etools disease records from conditionsdiseases.json's "disease" array (no filter).</summary>
    public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
    {
        var path = Path.Combine(fivetoolsDir, "conditionsdiseases.json");
        if (!File.Exists(path)) yield break;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllBytes(path)); }
        catch (JsonException) { yield break; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("disease", out var arr)
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
            Id: EntityIdSlug.For(sourceKey, EntityType.DiseasePoison, name),
            Type: EntityType.DiseasePoison,
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
    /// Builds the raw <c>fields</c> shape the
    /// <see cref="Features.Entities.CanonicalText.DiseasePoisonCanonicalTextRenderer"/> reads (and
    /// <c>Schemas/canonical/DiseasePoisonFields.schema.json</c> describes): a flattened
    /// <c>entries</c> array — the ONLY field this renderer consumes.
    /// </summary>
    private static JsonElement BuildFields(JsonElement disease)
    {
        var fields = new JsonObject
        {
            ["entries"] = FivetoolsEntryText.ToRendererEntries(disease),
        };

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }
}