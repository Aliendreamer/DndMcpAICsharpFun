using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.God"/>. Reads
/// <c>deities.json</c>'s <c>"deity"</c> array (no filter — every deity qualifies) and projects
/// the RAW <c>fields</c> shape the <see cref="Features.Entities.CanonicalText.GodCanonicalTextRenderer"/>
/// reads (<c>pantheon</c>/<c>symbol</c>/<c>alignment</c>/<c>domains</c> + <c>entries</c>) — NOT a
/// curated domain-record shape.
/// </summary>
public sealed class GodBackfillProvider : IFivetoolsBackfillProvider
{
    public EntityType Type => EntityType.God;

    /// <summary>Raw 5etools deity records from deities.json's "deity" array (no rarity-style
    /// filter — every deity is a candidate).</summary>
    public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
    {
        var path = Path.Combine(fivetoolsDir, "deities.json");
        if (!File.Exists(path)) yield break;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllBytes(path)); }
        catch (JsonException) { yield break; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("deity", out var arr)
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
            Id: EntityIdSlug.For(sourceKey, EntityType.God, name),
            Type: EntityType.God,
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
    /// <see cref="Features.Entities.CanonicalText.GodCanonicalTextRenderer"/> reads (and
    /// <c>Schemas/canonical/GodFields.schema.json</c> requires non-null): the raw
    /// <c>alignment</c>/<c>domains</c> arrays copied verbatim (defaulting to empty when absent —
    /// the schema requires them), <c>pantheon</c>/<c>symbol</c> strings, plus a flattened
    /// <c>entries</c> array (deities frequently have none).
    /// </summary>
    private static JsonElement BuildFields(JsonElement deity)
    {
        var fields = new JsonObject
        {
            ["alignment"] = RawFieldCopy.ArrayOrEmpty(deity, "alignment"),
            ["domains"] = RawFieldCopy.ArrayOrEmpty(deity, "domains"),
            ["symbol"] = RawFieldCopy.StringOrNull(deity, "symbol"),
            ["pantheon"] = RawFieldCopy.StringOrNull(deity, "pantheon"),
            ["entries"] = FivetoolsEntryText.ToRendererEntries(deity),
        };

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }
}