using System.Text.Json;
using System.Text.Json.Nodes;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// Shared VERBATIM raw-property copy helpers for backfill providers. Each backfilled entity's
/// <c>Fields</c> must carry the exact 5etools raw property names/shapes that the corresponding
/// <see cref="DndMcpAICsharpFun.Features.Entities.CanonicalText.ISimpleEntityRenderer"/> reads off
/// <c>envelope.Fields</c> (and that the hand-authored <c>Schemas/canonical/*Fields.schema.json</c>
/// overrides describe) — NOT a curated/derived domain shape. These helpers do no renaming, no
/// mapping, no computation: they clone the source property straight through when present and of
/// the expected JSON kind, else omit/null it.
/// </summary>
internal static class RawFieldCopy
{
    /// <summary>Copies a raw JSON array property verbatim (e.g. <c>prerequisite</c>,
    /// <c>skillProficiencies</c>, <c>alignment</c>, <c>domains</c>), else <c>null</c>.</summary>
    public static JsonNode? Array(JsonElement source, string prop)
        => source.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array
            ? JsonNode.Parse(v.GetRawText())
            : null;

    /// <summary>Copies a raw JSON array property verbatim, defaulting to an empty array when
    /// absent — for schema-required array fields (e.g. God's <c>alignment</c>/<c>domains</c>).</summary>
    public static JsonNode ArrayOrEmpty(JsonElement source, string prop)
        => Array(source, prop) ?? new JsonArray();

    /// <summary>Copies a raw string property verbatim, else <c>null</c>.</summary>
    public static string? StringOrNull(JsonElement source, string prop)
        => source.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>Copies a raw numeric property verbatim as an <c>int</c>, else <c>null</c>.</summary>
    public static int? IntOrNull(JsonElement source, string prop)
        => source.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n
            : null;

    /// <summary>Copies a raw property of ANY JSON kind verbatim (e.g. MagicItem's <c>reqAttune</c>,
    /// which may be a bool or a string), else <c>null</c>.</summary>
    public static JsonNode? Any(JsonElement source, string prop)
        => source.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Undefined && v.ValueKind != JsonValueKind.Null
            ? JsonNode.Parse(v.GetRawText())
            : null;
}
