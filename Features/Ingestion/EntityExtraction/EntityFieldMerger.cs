using System.Text.Json;
using System.Text.Json.Nodes;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Merges ordered partial JSON field objects from chunked extraction into one object.
/// Scalars/objects: first non-null wins. Arrays: concatenated in chunk order,
/// deduplicated by "name" property when present. No entity-type awareness.
/// </summary>
public sealed class EntityFieldMerger
{
    public JsonElement Merge(IList<JsonElement> partials)
    {
        if (partials.Count == 1)
            return partials[0];

        var result = new JsonObject();

        foreach (var partial in partials)
        {
            if (partial.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var prop in partial.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Null)
                    continue;

                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    var target = result[prop.Name] as JsonArray;
                    if (target is null)
                    {
                        target = [];
                        result[prop.Name] = target;
                    }
                    foreach (var item in prop.Value.EnumerateArray())
                        AppendIfNotDuplicate(target, item);
                }
                else if (!result.ContainsKey(prop.Name))
                {
                    result[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
                }
            }
        }

        return JsonDocument.Parse(result.ToJsonString()).RootElement.Clone();
    }

    private static void AppendIfNotDuplicate(JsonArray target, JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("name", out var nameProp) &&
            nameProp.ValueKind == JsonValueKind.String)
        {
            var name = nameProp.GetString();
            foreach (var existing in target)
            {
                if (existing is JsonObject obj &&
                    obj.TryGetPropertyValue("name", out var existingName) &&
                    existingName?.GetValue<string>() == name)
                {
                    return;
                }
            }
        }
        target.Add(JsonNode.Parse(item.GetRawText()));
    }
}
