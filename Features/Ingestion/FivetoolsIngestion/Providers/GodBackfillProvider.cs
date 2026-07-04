using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.God"/>. Reads
/// <c>deities.json</c>'s <c>"deity"</c> array (no filter — every deity qualifies) and projects a
/// curated <see cref="Domain.Entities.Fields.GodFields"/> shape — a NEW projection (the generic
/// mapper's Clone doesn't match this canonical shape).
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
        var srd   = element.TryGetProperty("srd",   out var s)  && s.ValueKind  == JsonValueKind.True;
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
    /// Builds the canonical God <c>fields</c> shape (see
    /// <see cref="Domain.Entities.Fields.GodFields"/>): alignment codes joined, domains list,
    /// optional symbol/pantheon/plane, and a description assembled from the string entries of
    /// <c>entries[]</c> (deities frequently have none).
    /// </summary>
    private static JsonElement BuildFields(JsonElement deity)
    {
        var fields = new JsonObject
        {
            ["alignment"] = GetAlignment(deity),
            ["domains"] = GetDomains(deity),
            ["symbol"] = CopyStringOrNull(deity, "symbol"),
            ["pantheon"] = CopyStringOrNull(deity, "pantheon"),
            ["plane"] = CopyStringOrNull(deity, "plane"),
            ["description"] = GetDescription(deity),
        };

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }

    private static string GetAlignment(JsonElement deity)
    {
        if (!deity.TryGetProperty("alignment", out var v) || v.ValueKind != JsonValueKind.Array)
            return "";
        return string.Join(", ", v.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!));
    }

    private static JsonArray GetDomains(JsonElement deity)
    {
        var domains = new JsonArray();
        if (deity.TryGetProperty("domains", out var v) && v.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in v.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String)
                    domains.Add(e.GetString());
        }
        return domains;
    }

    private static string? CopyStringOrNull(JsonElement deity, string prop)
        => deity.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string GetDescription(JsonElement deity)
    {
        if (!deity.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return "";
        return string.Join("\n\n", entries.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!));
    }
}
