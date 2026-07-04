using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.Entities;

public static class EntityMerger
{
    // ── Narrative-key allowlist ───────────────────────────────────────────────
    // Keys whose canonical (LLM) value always wins over the 5etools value.
    // These carry prose/lore that our extraction pipeline produces and that we
    // never want overwritten by 5etools' terse text or tag arrays.
    private static readonly FrozenSet<string> NarrativeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "entries",
        "description",
        "text",
        // Pattern-suffix: any key ending in "Entries" is also narrative (e.g. classEntries,
        // subclassEntries, featureEntries). These are checked via IsNarrativeKey() below.
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="key"/> is a narrative key whose
    /// canonical (LLM) value should be preserved over the 5etools value.
    /// </summary>
    private static bool IsNarrativeKey(string key) =>
        NarrativeKeys.Contains(key) ||
        key.EndsWith("Entries", StringComparison.OrdinalIgnoreCase);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Merges <paramref name="canonical"/> (LLM-extracted) with <paramref name="existing"/>
    /// (typically a 5etools file record or a prior Qdrant point).
    ///
    /// Merge rules (per spec):
    /// <list type="bullet">
    ///   <item><c>fields</c> — recursive deep merge: narrative keys (prose) from canonical;
    ///     all other keys from existing when present and non-empty; missing keys kept from
    ///     canonical.</item>
    ///   <item><c>canonicalText</c> — canonical always wins.</item>
    ///   <item><c>name</c> — existing clean name unless canonical.DataSource == "manual".</item>
    ///   <item><c>type</c> — existing when canonical type is Class.</item>
    ///   <item><c>srd/srd52/basicRules2024</c> — existing when existing.DataSource == "5etools".</item>
    ///   <item><c>keywords</c> — union of both.</item>
    ///   <item><c>page</c> — existing when set.</item>
    ///   <item><c>DataSource</c> — "llm" unless canonical was "manual".</item>
    /// </list>
    /// This method is pure — neither input is mutated.
    /// </summary>
    public static EntityEnvelope Merge(EntityEnvelope canonical, EntityEnvelope existing)
    {
        var type = canonical.Type == EntityType.Class
            ? existing.Type
            : canonical.Type;

        // Keywords: union of both sets (order: existing first, then canonical extras).
        var keywords = existing.Keywords
            .Union(canonical.Keywords, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var page = existing.Page ?? canonical.Page;

        var srd            = existing.DataSource == "5etools" ? existing.Srd            : canonical.Srd;
        var srd52          = existing.DataSource == "5etools" ? existing.Srd52          : canonical.Srd52;
        var basicRules2024 = existing.DataSource == "5etools" ? existing.BasicRules2024 : canonical.BasicRules2024;

        // Name: existing clean name wins unless the canonical record is manual.
        var name = canonical.DataSource == "manual" ? canonical.Name : existing.Name;

        // DataSource: preserve "manual" from canonical; otherwise stamp "llm".
        var dataSource = canonical.DataSource == "manual" ? "manual" : "llm";

        // Fields: recursive deep-merge.
        var mergedFields = DeepMergeFields(canonical.Fields, existing.Fields);

        return canonical with
        {
            Type           = type,
            Name           = name,
            Srd            = srd,
            Srd52          = srd52,
            BasicRules2024 = basicRules2024,
            Keywords       = keywords,
            Page           = page,
            DataSource     = dataSource,
            Fields         = mergedFields,
            // canonicalText stays from canonical (it will be re-rendered by the orchestrator).
        };
    }

    // ── Deep-merge helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Recursively merges two <see cref="JsonElement"/> objects key-by-key.
    /// <list type="bullet">
    ///   <item>Narrative keys → canonical wins.</item>
    ///   <item>Non-narrative keys → existing wins when present and non-empty; else canonical kept.</item>
    ///   <item>Object values → merged recursively.</item>
    ///   <item>Non-object / non-narrative arrays → existing wins wholesale.</item>
    /// </list>
    /// Returns a fresh <see cref="JsonElement"/> (backed by a clone); inputs are not mutated.
    /// </summary>
    private static JsonElement DeepMergeFields(JsonElement canonical, JsonElement existing)
    {
        // If either side is not an Object, fall back to canonical (can't deep-merge scalars/arrays).
        if (canonical.ValueKind != JsonValueKind.Object
            || existing.ValueKind != JsonValueKind.Object)
        {
            return canonical.Clone();
        }

        var result = new JsonObject();

        // Start from canonical keys, applying merge rules.
        foreach (var prop in canonical.EnumerateObject())
        {
            if (IsNarrativeKey(prop.Name))
            {
                // Narrative → our value always wins.
                result[prop.Name] = ToJsonNode(prop.Value);
            }
            else if (existing.TryGetProperty(prop.Name, out var existingVal)
                     && !IsEmpty(existingVal))
            {
                // Non-narrative & existing has a non-empty value → 5etools wins.
                // If both sides are objects, recurse.
                if (prop.Value.ValueKind == JsonValueKind.Object
                    && existingVal.ValueKind == JsonValueKind.Object)
                {
                    result[prop.Name] = ToJsonNode(DeepMergeFields(prop.Value, existingVal));
                }
                else
                {
                    result[prop.Name] = ToJsonNode(existingVal);
                }
            }
            else
            {
                // Existing lacks this key (or it is empty) → keep canonical.
                result[prop.Name] = ToJsonNode(prop.Value);
            }
        }

        // Add keys from existing that canonical didn't have (fills missing structured fields).
        foreach (var prop in existing.EnumerateObject())
        {
            if (result.ContainsKey(prop.Name))
                continue; // already handled above

            if (!IsNarrativeKey(prop.Name) && !IsEmpty(prop.Value))
                result[prop.Name] = ToJsonNode(prop.Value);
        }

        // Serialise the JsonObject back to a JsonElement so the return type is stable.
        var jsonString = result.ToJsonString();
        return JsonDocument.Parse(jsonString).RootElement.Clone();
    }

    /// <summary>Returns <see langword="true"/> when the element is considered "empty"
    /// (Undefined, Null, empty string, or empty array/object).</summary>
    private static bool IsEmpty(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Undefined => true,
        JsonValueKind.Null      => true,
        JsonValueKind.String    => string.IsNullOrEmpty(el.GetString()),
        JsonValueKind.Array     => el.GetArrayLength() == 0,
        JsonValueKind.Object    => !el.EnumerateObject().Any(),
        _                       => false,
    };

    /// <summary>Converts a <see cref="JsonElement"/> into a <see cref="System.Text.Json.Nodes.JsonNode"/>
    /// so it can be placed into a <see cref="System.Text.Json.Nodes.JsonObject"/>.</summary>
    private static JsonNode? ToJsonNode(JsonElement el)
        => JsonNode.Parse(el.GetRawText());
}
