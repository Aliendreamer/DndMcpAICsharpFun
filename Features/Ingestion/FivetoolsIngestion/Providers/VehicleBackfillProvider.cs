using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.VehicleMount"/>. Reads
/// <c>vehicles.json</c>'s <c>"vehicle"</c> array (no filter — every vehicle/mount qualifies; the
/// sibling <c>"vehicleUpgrade"</c> array has no modeled <see cref="EntityType"/> and is out of
/// scope here) and projects a curated <see cref="Domain.Entities.Fields.VehicleMountFields"/>
/// shape — self-contained, like <see cref="GodBackfillProvider"/>/<see cref="SpellBackfillProvider"/>,
/// NOT the generic field-fill mapper's raw clone.
/// </summary>
public sealed class VehicleBackfillProvider : IFivetoolsBackfillProvider
{
    public EntityType Type => EntityType.VehicleMount;

    /// <summary>Raw 5etools vehicle records from vehicles.json's "vehicle" array (no filter).</summary>
    public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
    {
        var path = Path.Combine(fivetoolsDir, "vehicles.json");
        if (!File.Exists(path)) yield break;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllBytes(path)); }
        catch (JsonException) { yield break; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("vehicle", out var arr)
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
            Id: EntityIdSlug.For(sourceKey, EntityType.VehicleMount, name),
            Type: EntityType.VehicleMount,
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
    /// Builds the canonical VehicleMount <c>fields</c> shape (see
    /// <see cref="Domain.Entities.Fields.VehicleMountFields"/>): the raw <c>vehicleType</c> code
    /// as <c>kind</c>, a best-effort walking/first-listed speed in feet, a best-effort cargo
    /// capacity in pounds (5etools' <c>capCargo</c> is denominated in TONS, converted ×2000), and
    /// a description assembled from <c>entries[]</c>.
    /// </summary>
    private static JsonElement BuildFields(JsonElement vehicle)
    {
        var fields = new JsonObject
        {
            ["kind"] = GetKind(vehicle),
            ["speed"] = CopySpeedOrNull(vehicle),
            ["capacityLb"] = CopyCapacityLbOrNull(vehicle),
            ["description"] = GetDescription(vehicle),
        };

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }

    private static string GetKind(JsonElement vehicle)
        => vehicle.TryGetProperty("vehicleType", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : "";

    private static JsonNode? CopySpeedOrNull(JsonElement vehicle)
    {
        if (!vehicle.TryGetProperty("speed", out var speed))
            return null;

        if (speed.ValueKind == JsonValueKind.Number && speed.TryGetInt32(out var n))
            return JsonValue.Create(n);

        if (speed.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in speed.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var pv))
                    return JsonValue.Create(pv);
                if (prop.Value.ValueKind == JsonValueKind.Object
                    && prop.Value.TryGetProperty("number", out var num)
                    && num.ValueKind == JsonValueKind.Number
                    && num.TryGetInt32(out var nv))
                    return JsonValue.Create(nv);
            }
        }

        return null;
    }

    private static JsonNode? CopyCapacityLbOrNull(JsonElement vehicle)
        => vehicle.TryGetProperty("capCargo", out var c)
            && c.ValueKind == JsonValueKind.Number
            && c.TryGetDouble(out var tons)
            ? JsonValue.Create((int)Math.Round(tons * 2000))
            : null;

    private static string GetDescription(JsonElement vehicle)
    {
        if (!vehicle.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return "";
        return FivetoolsEntryText.Flatten(entries);
    }
}