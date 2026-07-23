using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.Feat"/>. Reads
/// <c>feats.json</c>'s <c>"feat"</c> array (no filter — every feat qualifies) and projects the
/// RAW <c>fields</c> shape the <see cref="Features.Entities.CanonicalText.FeatCanonicalTextRenderer"/>
/// reads (<c>prerequisite</c> + <c>entries</c>) — NOT a curated domain-record shape.
/// </summary>
public sealed class FeatBackfillProvider : IFivetoolsBackfillProvider
{
    public EntityType Type => EntityType.Feat;

    /// <summary>Raw 5etools feat records from feats.json's "feat" array (no filter — every feat is a candidate).</summary>
    public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
    {
        var path = Path.Combine(fivetoolsDir, "feats.json");
        if (!File.Exists(path)) yield break;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllBytes(path)); }
        catch (JsonException) { yield break; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("feat", out var arr)
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
            Id: EntityIdSlug.For(sourceKey, EntityType.Feat, name),
            Type: EntityType.Feat,
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
    /// Builds the raw <c>fields</c> shape the <see cref="Features.Entities.CanonicalText.FeatCanonicalTextRenderer"/>
    /// reads (and <c>Schemas/canonical/FeatFields.schema.json</c> describes): the raw
    /// <c>prerequisite</c> array copied verbatim (the renderer serialises it as-is), plus a
    /// flattened <c>entries</c> array (see <see cref="FivetoolsEntryText.ToRendererEntries"/>).
    /// </summary>
    private static JsonElement BuildFields(JsonElement feat)
    {
        var fields = new JsonObject
        {
            ["prerequisite"] = RawFieldCopy.Array(feat, "prerequisite"),
            ["entries"] = FivetoolsEntryText.ToRendererEntries(feat),
        };

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }
}