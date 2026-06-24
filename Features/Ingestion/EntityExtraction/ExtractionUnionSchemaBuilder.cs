using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Builds the discriminated-union (<c>oneOf</c>) extraction schema for the content-first
/// extraction path: one branch per offered entity type (each with a <c>const</c>
/// <c>entityType</c> discriminator and that type's fields) plus a decline branch
/// <c>{"entityType":"none","reason":...}</c>. The decline branch is ALWAYS included, so a
/// mis-pruned branch set degrades to a decline rather than a forced fabrication.
/// Validated on Ollama/<c>qwen3:8b</c> by the <c>oneof-decoding-spike</c> change.
/// </summary>
public static class ExtractionUnionSchemaBuilder
{
    /// <summary>The discriminator value of the decline branch.</summary>
    public const string DeclineType = "none";

    /// <summary>
    /// Builds the union from the offered <paramref name="branches"/> (in order) using the
    /// per-type field schemas in <paramref name="perTypeSchemas"/>. Branch types absent from
    /// the dictionary are skipped. The decline branch is always appended.
    /// </summary>
    public static JsonElement Build(
        IReadOnlyList<EntityType> branches,
        IReadOnlyDictionary<EntityType, JsonElement> perTypeSchemas)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WritePropertyName("oneOf");
            writer.WriteStartArray();

            var seen = new HashSet<EntityType>();
            foreach (var type in branches)
            {
                if (!seen.Add(type)) continue;
                if (!perTypeSchemas.TryGetValue(type, out var schema)) continue;
                WriteTypeBranch(writer, type, schema);
            }

            WriteDeclineBranch(writer);

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(ms.ToArray());
        return doc.RootElement.Clone();
    }

    private static void WriteTypeBranch(Utf8JsonWriter writer, EntityType type, JsonElement schema)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);

        writer.WritePropertyName("required");
        writer.WriteStartArray();
        writer.WriteStringValue("entityType");
        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            foreach (var r in required.EnumerateArray())
                r.WriteTo(writer);
        writer.WriteEndArray();

        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        writer.WritePropertyName("entityType");
        writer.WriteStartObject();
        writer.WriteString("const", type.ToString());
        writer.WriteEndObject();
        if (schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            foreach (var p in props.EnumerateObject())
                p.WriteTo(writer);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    private static void WriteDeclineBranch(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);

        writer.WritePropertyName("required");
        writer.WriteStartArray();
        writer.WriteStringValue("entityType");
        writer.WriteStringValue("reason");
        writer.WriteEndArray();

        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        writer.WritePropertyName("entityType");
        writer.WriteStartObject();
        writer.WriteString("const", DeclineType);
        writer.WriteEndObject();
        writer.WritePropertyName("reason");
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteEndObject();
    }
}
