using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Loads per-entity-type JSON schemas from disk and injects the synthetic
/// <c>confidence</c> field that the LLM is asked to populate.
/// </summary>
public sealed class EntitySchemaProvider(
    IOptions<EntityExtractionOptions> options,
    ILogger<EntitySchemaProvider> logger)
{
    private readonly EntityExtractionOptions _opts = options.Value;

    public Dictionary<EntityType, JsonElement> LoadSchemas()
    {
        var dict = new Dictionary<EntityType, JsonElement>();
        foreach (var type in Enum.GetValues<EntityType>())
        {
            var path = Path.Combine(_opts.SchemasDirectory, $"{type}Fields.schema.json");
            try
            {
                using var stream = File.OpenRead(path);
                using var doc    = JsonDocument.Parse(stream);
                dict[type] = InjectConfidenceField(doc.RootElement.Clone());
            }
            catch (FileNotFoundException)
            {
                logger.LogDebug("Schema file not found for {Type} at {Path}; type will be skipped", type, path);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Schema file for {Type} at {Path} is malformed; type will be skipped", type, path);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not read schema file for {Type} at {Path}; type will be skipped", type, path);
            }
        }
        return dict;
    }

    internal static JsonElement InjectConfidenceField(JsonElement schema)
    {
        using var ms     = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);
        writer.WriteStartObject();
        foreach (var prop in schema.EnumerateObject())
        {
            if (prop.Name == "properties")
            {
                writer.WritePropertyName("properties");
                writer.WriteStartObject();
                foreach (var p in prop.Value.EnumerateObject())
                    p.WriteTo(writer);
                writer.WritePropertyName("confidence");
                writer.WriteRawValue("{\"type\":\"string\",\"enum\":[\"low\",\"medium\",\"high\"]}");
                writer.WriteEndObject();
            }
            else
            {
                prop.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
        writer.Flush();
        using var doc = JsonDocument.Parse(ms.ToArray());
        return doc.RootElement.Clone();
    }
}
