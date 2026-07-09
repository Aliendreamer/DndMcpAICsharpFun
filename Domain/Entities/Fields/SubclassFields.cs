using System.Text.Json;
using System.Text.Json.Serialization;

namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record SubclassFields(
    [property: JsonPropertyName("className")] string ClassName,
    [property: JsonPropertyName("classSource")] string? ClassSource,
    [property: JsonPropertyName("shortName")] string? ShortName,
    [property: JsonPropertyName("subclassFeatures")] IReadOnlyList<JsonElement>? SubclassFeatures,
    [property: JsonPropertyName("entries")] IReadOnlyList<JsonElement>? Entries);