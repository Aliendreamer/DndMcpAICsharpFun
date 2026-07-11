using System.Text.Json;
using System.Text.Json.Nodes;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>Fill-missing-only merge of allowlisted 5etools fields onto one entity's Fields. Never
/// overwrites an extraction/human field; re-derives previously-5etools-filled fields (deterministic);
/// records provenance in a reserved <c>_fivetoolsFilledFields</c> array. Idempotent.</summary>
public static class EntityFieldMerger
{
    private const string ProvenanceKey = "_fivetoolsFilledFields";

    public static (JsonElement Fields, bool Changed) Merge(
        JsonElement entityFields, IReadOnlySet<string> allowlist, JsonElement fivetoolsFields)
    {
        var obj = entityFields.ValueKind == JsonValueKind.Object
            ? (JsonObject)JsonNode.Parse(entityFields.GetRawText())!
            : new JsonObject();

        var filled = new SortedSet<string>(StringComparer.Ordinal);
        if (obj[ProvenanceKey] is JsonArray existingProv)
            foreach (var n in existingProv) if (n?.GetValue<string>() is { } s) filled.Add(s);

        var changed = false;
        foreach (var field in allowlist)
        {
            if (fivetoolsFields.ValueKind != JsonValueKind.Object
                || !fivetoolsFields.TryGetProperty(field, out var incoming))
                continue;                                   // 5etools doesn't have it → nothing to fill

            var present = obj.ContainsKey(field);
            if (present && !filled.Contains(field))
                continue;                                   // extraction/human produced it → never touch

            var newVal = JsonNode.Parse(incoming.GetRawText());
            var before = obj[field]?.ToJsonString();
            obj[field] = newVal;
            filled.Add(field);
            if (before != obj[field]?.ToJsonString()) changed = true;
        }

        // Re-write provenance last (sorted) so re-runs are byte-identical.
        obj.Remove(ProvenanceKey);
        if (filled.Count > 0)
            obj[ProvenanceKey] = new JsonArray(filled.Select(f => (JsonNode)f!).ToArray());

        var result = JsonDocument.Parse(obj.ToJsonString()).RootElement.Clone();
        return (result, changed);
    }
}
