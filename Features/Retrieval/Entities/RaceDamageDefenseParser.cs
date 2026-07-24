using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

/// <summary>
/// The set of damage-type codes a race resists / is immune to, from its raw entity <c>Fields</c> —
/// direct string entries AND each <c>choose.from</c> entry, under the given key (<c>resist</c> or
/// <c>immune</c>). Reads the raw JsonElement (the typed DeserialiseFields throws on drift); case-tolerant
/// on the key; ValueKind-guarded; never throws. Empty when there is no data for that key.
/// Sibling of RaceAbilityParser (which reads numeric ability keys; here the entries are damage-type strings).
/// </summary>
public static class RaceDamageDefenseParser
{
    public static IReadOnlySet<string> DamageTypes(JsonElement fields, string key)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (fields.ValueKind != JsonValueKind.Object) return result;

        // Case-tolerant lookup of the key array (TryGetProperty is case-sensitive).
        var arr = default(JsonElement);
        var found = false;
        foreach (var prop in fields.EnumerateObject())
            if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
            {
                arr = prop.Value;
                found = true;
                break;
            }
        if (!found || arr.ValueKind != JsonValueKind.Array) return result;

        foreach (var entry in arr.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                if (entry.GetString() is { } s && !string.IsNullOrWhiteSpace(s)) result.Add(s.ToLowerInvariant());
            }
            else if (entry.ValueKind == JsonValueKind.Object
                     && entry.TryGetProperty("choose", out var choose)
                     && choose.ValueKind == JsonValueKind.Object
                     && choose.TryGetProperty("from", out var from)
                     && from.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in from.EnumerateArray())
                    if (f.ValueKind == JsonValueKind.String && f.GetString() is { } d && !string.IsNullOrWhiteSpace(d))
                        result.Add(d.ToLowerInvariant());
            }
        }
        return result;
    }
}