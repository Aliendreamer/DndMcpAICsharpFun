using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

/// <summary>
/// The set of ability codes (str/dex/con/int/wis/cha) a race boosts, parsed from its raw entity
/// <c>Fields</c> — fixed bonuses (a numeric ability key) AND choosable bonuses (each `choose.from`
/// entry). Reads the raw JsonElement (the typed DeserialiseFields throws on drift); case-tolerant on
/// the `ability` key; ValueKind-guarded; never throws. Empty when there is no ability data.
/// </summary>
public static class RaceAbilityParser
{
    private static readonly HashSet<string> AbilityCodes = new(StringComparer.OrdinalIgnoreCase)
        { "str", "dex", "con", "int", "wis", "cha" };

    public static IReadOnlySet<string> BoostedAbilities(JsonElement fields)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (fields.ValueKind != JsonValueKind.Object) return result;

        // Case-tolerant lookup of the `ability` array (TryGetProperty is case-sensitive).
        var ability = default(JsonElement);
        var found = false;
        foreach (var prop in fields.EnumerateObject())
            if (string.Equals(prop.Name, "ability", StringComparison.OrdinalIgnoreCase))
            {
                ability = prop.Value;
                found = true;
                break;
            }
        if (!found || ability.ValueKind != JsonValueKind.Array) return result;

        foreach (var entry in ability.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in entry.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && AbilityCodes.Contains(prop.Name))
                {
                    result.Add(prop.Name.ToLowerInvariant());
                }
                else if (string.Equals(prop.Name, "choose", StringComparison.OrdinalIgnoreCase)
                         && prop.Value.ValueKind == JsonValueKind.Object
                         && prop.Value.TryGetProperty("from", out var from)
                         && from.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in from.EnumerateArray())
                        if (f.ValueKind == JsonValueKind.String && f.GetString() is { } code && AbilityCodes.Contains(code))
                            result.Add(code.ToLowerInvariant());
                }
            }
        }
        return result;
    }
}