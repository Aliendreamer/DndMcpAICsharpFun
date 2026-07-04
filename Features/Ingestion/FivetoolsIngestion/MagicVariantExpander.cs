using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>
/// Expands 5etools <c>magicvariant</c> templates (<c>magicvariants.json</c>) — e.g. "+1 Weapon" — into
/// concrete synthetic magic-item <see cref="JsonElement"/>s, one per matching base item from
/// <c>items-base.json</c>'s <c>baseitem</c> pool. 5etools stores templated <c>+N</c> items this way
/// rather than as concrete <c>items.json</c> records, so without this expansion recall would never
/// match extracted names like "+1 Longsword" and backfill would never fill the gap.
/// </summary>
/// <remarks>
/// Pure function of the two source files; no state. Synthetic items are produced across ALL sources —
/// each tagged with its own <c>inherits.source</c> (which can differ variant to variant) — so callers
/// apply their own source/rarity filtering (mirroring how <c>items.json</c> records are consumed).
/// </remarks>
public static partial class MagicVariantExpander
{
    /// <summary>Matches <c>{=key}</c> or <c>{=key/mod}</c> placeholder tokens in <c>inherits.entries</c>;
    /// the optional <c>/mod</c> suffix (e.g. a grammar hint like lowercase or article) is ignored — the
    /// resolved value is substituted as-is.</summary>
    [GeneratedRegex(@"\{=([A-Za-z0-9_]+)(?:/[A-Za-z0-9_]+)?\}")]
    private static partial Regex PlaceholderToken();

    public static IEnumerable<JsonElement> Expand(string fivetoolsDir)
    {
        var variants = ReadArray(fivetoolsDir, "magicvariants.json", "magicvariant");
        if (variants.Count == 0) yield break;

        var baseItems = ReadArray(fivetoolsDir, "items-base.json", "baseitem");
        if (baseItems.Count == 0) yield break;

        foreach (var variant in variants)
        {
            if (!variant.TryGetProperty("inherits", out var inherits) || inherits.ValueKind != JsonValueKind.Object)
                continue;

            var requires = variant.TryGetProperty("requires", out var req) ? req : default;
            var excludes = variant.TryGetProperty("excludes", out var exc) ? exc : default;

            foreach (var baseItem in baseItems)
            {
                if (!AnyPredicateMatches(baseItem, requires)) continue;
                if (AnyPredicateMatches(baseItem, excludes)) continue;

                yield return BuildSyntheticItem(baseItem, inherits);
            }
        }
    }

    /// <summary>Reads a top-level JSON array property from a 5etools data file. Returns an empty list
    /// when the file is absent, unparsable, or the property is missing/not an array.</summary>
    private static List<JsonElement> ReadArray(string dir, string fileName, string arrayProperty)
    {
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path)) return [];

        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllBytes(path)); }
        catch (JsonException) { return []; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty(arrayProperty, out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];

            return arr.EnumerateArray().Select(e => e.Clone()).ToList();
        }
    }

    /// <summary>
    /// <c>requires</c>/<c>excludes</c> are a list of predicate objects — matches if ANY entry matches
    /// (an entry matches if EVERY key/value pair matches the base item). A single predicate object
    /// (rather than a list of one) is also accepted, since 5etools' real <c>excludes</c> shape is a
    /// bare object rather than a list. Absent/undefined never matches.
    /// </summary>
    private static bool AnyPredicateMatches(JsonElement baseItem, JsonElement predicateHolder)
    {
        switch (predicateHolder.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var entry in predicateHolder.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Object && PredicateMatches(baseItem, entry))
                        return true;
                }
                return false;
            case JsonValueKind.Object:
                return PredicateMatches(baseItem, predicateHolder);
            default:
                return false;
        }
    }

    private static bool PredicateMatches(JsonElement baseItem, JsonElement predicate)
    {
        foreach (var prop in predicate.EnumerateObject())
        {
            if (!baseItem.TryGetProperty(prop.Name, out var actual)) return false;
            if (!ValueMatches(prop.Name, prop.Value, actual)) return false;
        }
        return true;
    }

    /// <summary><c>type</c> is compared after stripping a trailing <c>|SOURCE</c> suffix from both
    /// sides (5etools tags some requires/type values with a source, e.g. <c>"AF|DMG"</c>); every other
    /// key is compared by value (string/bool/number).</summary>
    private static bool ValueMatches(string key, JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind == JsonValueKind.Array)
        {
            // Array-valued expected value means "matches if the base item's field equals ANY
            // of the array elements" (contains-any-of semantics). The base item's actual field
            // may itself be a scalar (e.g. name) or an array (e.g. property on a weapon), in
            // which case a match requires the intersection to be non-empty.
            if (actual.ValueKind == JsonValueKind.Array)
            {
                foreach (var actualElement in actual.EnumerateArray())
                {
                    foreach (var expectedElement in expected.EnumerateArray())
                    {
                        if (ScalarValueMatches(key, expectedElement, actualElement)) return true;
                    }
                }
                return false;
            }

            foreach (var expectedElement in expected.EnumerateArray())
            {
                if (ScalarValueMatches(key, expectedElement, actual)) return true;
            }
            return false;
        }

        if (actual.ValueKind == JsonValueKind.Array)
        {
            // Scalar-valued expected value against an array-valued base-item field (e.g. a
            // variant's scalar "property" predicate matched against a weapon's "property"
            // array): match if the scalar equals ANY element of the actual array.
            foreach (var actualElement in actual.EnumerateArray())
            {
                if (ScalarValueMatches(key, expected, actualElement)) return true;
            }
            return false;
        }

        return ScalarValueMatches(key, expected, actual);
    }


    private static bool ScalarValueMatches(string key, JsonElement expected, JsonElement actual)
    {
        if (key == "type")
        {
            return string.Equals(
                StripSource(expected.ValueKind == JsonValueKind.String ? expected.GetString() : null),
                StripSource(actual.ValueKind == JsonValueKind.String ? actual.GetString() : null),
                StringComparison.Ordinal);
        }

        return expected.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False => actual.ValueKind == expected.ValueKind,
            JsonValueKind.String => actual.ValueKind == JsonValueKind.String
                && actual.GetString() == expected.GetString(),
            JsonValueKind.Number => actual.ValueKind == JsonValueKind.Number
                && actual.GetDouble() == expected.GetDouble(),
            _ => false,
        };
    }

    private static string StripSource(string? value)
    {
        if (value is null) return "";
        var pipe = value.IndexOf('|');
        return pipe >= 0 ? value[..pipe] : value;
    }

    /// <summary>Builds the synthetic item: name = namePrefix+baseName+nameSuffix; source from
    /// <c>inherits.source</c>; page/rarity/reqAttune/srd/tier copied from <c>inherits</c> when present;
    /// type from the base item; entries deep-copied from <c>inherits.entries</c> with placeholder
    /// tokens substituted.</summary>
    private static JsonElement BuildSyntheticItem(JsonElement baseItem, JsonElement inherits)
    {
        var baseName = GetString(baseItem, "name") ?? "";
        var namePrefix = GetString(inherits, "namePrefix") ?? "";
        var nameSuffix = GetString(inherits, "nameSuffix") ?? "";

        var obj = new JsonObject
        {
            ["name"] = namePrefix + baseName + nameSuffix,
            ["source"] = GetString(inherits, "source") ?? "",
            ["type"] = GetString(baseItem, "type") ?? "",
        };

        CopyIfPresent(inherits, obj, "page");
        CopyIfPresent(inherits, obj, "rarity");
        CopyIfPresent(inherits, obj, "reqAttune");
        CopyIfPresent(inherits, obj, "srd");
        CopyIfPresent(inherits, obj, "tier");

        if (inherits.TryGetProperty("entries", out var entries))
        {
            var substituted = SubstitutePlaceholders(entries, inherits, baseName);
            obj["entries"] = JsonNode.Parse(substituted.GetRawText());
        }

        return JsonDocument.Parse(obj.ToJsonString()).RootElement.Clone();
    }

    private static void CopyIfPresent(JsonElement source, JsonObject target, string key)
    {
        if (source.TryGetProperty(key, out var value))
            target[key] = JsonNode.Parse(value.GetRawText());
    }

    private static string? GetString(JsonElement element, string key)
        => element.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// Substitutes every <c>{=key}</c>/<c>{=key/mod}</c> token in <paramref name="entries"/> with
    /// <c>inherits[key]</c>'s string value (<paramref name="baseName"/> for <c>{=baseName}</c>; empty
    /// string when the key is absent from <c>inherits</c>). Operates on the raw JSON text so arbitrarily
    /// nested <c>entries</c> shapes (lists, tables, item blocks) are substituted uniformly, then
    /// re-parses — yielding an independent deep copy.
    /// </summary>
    private static JsonElement SubstitutePlaceholders(JsonElement entries, JsonElement inherits, string baseName)
    {
        var raw = entries.GetRawText();
        var substituted = PlaceholderToken().Replace(raw, match =>
        {
            var key = match.Groups[1].Value;
            var resolved = key == "baseName" ? baseName : ResolveInheritsValue(inherits, key);
            // The token always sits inside a JSON string literal, so splice in a JSON-escaped value.
            var quoted = JsonSerializer.Serialize(resolved);
            return quoted[1..^1];
        });

        return JsonDocument.Parse(substituted).RootElement.Clone();
    }

    private static string ResolveInheritsValue(JsonElement inherits, string key)
    {
        if (!inherits.TryGetProperty(key, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };
    }
}
