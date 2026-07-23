using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.Background"/>. Reads
/// <c>backgrounds.json</c>'s <c>"background"</c> array (no filter — every background qualifies)
/// and projects the RAW <c>fields</c> shape the
/// <see cref="Features.Entities.CanonicalText.BackgroundCanonicalTextRenderer"/> reads
/// (<c>skillProficiencies</c> + <c>entries</c>) — NOT a curated domain-record shape.
/// </summary>
public sealed class BackgroundBackfillProvider : IFivetoolsBackfillProvider
{
    public EntityType Type => EntityType.Background;

    /// <summary>Raw 5etools background records from backgrounds.json's "background" array (no filter).</summary>
    public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
    {
        var path = Path.Combine(fivetoolsDir, "backgrounds.json");
        if (!File.Exists(path)) yield break;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllBytes(path)); }
        catch (JsonException) { yield break; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("background", out var arr)
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
            Id: EntityIdSlug.For(sourceKey, EntityType.Background, name),
            Type: EntityType.Background,
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
    /// <see cref="Features.Entities.CanonicalText.BackgroundCanonicalTextRenderer"/> reads (and
    /// <c>Schemas/canonical/BackgroundFields.schema.json</c> describes): the raw
    /// <c>skillProficiencies</c> array (array of objects, e.g. <c>[{"insight":true}]</c>) copied
    /// verbatim, plus a flattened <c>entries</c> array.
    /// </summary>
    private static JsonElement BuildFields(JsonElement background)
    {
        var fields = new JsonObject
        {
            ["skillProficiencies"] = RawFieldCopy.Array(background, "skillProficiencies"),
            ["entries"] = FivetoolsEntryText.ToRendererEntries(background),
        };

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }
}