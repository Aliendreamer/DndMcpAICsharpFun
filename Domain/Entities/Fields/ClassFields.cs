using System.Text.Json;
using System.Text.Json.Serialization;

namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record HitDice(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("faces")]  int Faces);

public sealed record ClassFields(
    [property: JsonPropertyName("hd")]                   HitDice? Hd,
    [property: JsonPropertyName("proficiency")]           IReadOnlyList<string>? Proficiency,
    [property: JsonPropertyName("startingProficiencies")] JsonElement? StartingProficiencies,
    [property: JsonPropertyName("classFeatures")]         IReadOnlyList<JsonElement>? ClassFeatures,
    [property: JsonPropertyName("multiclassing")]         JsonElement? Multiclassing,
    [property: JsonPropertyName("entries")]               IReadOnlyList<JsonElement>? Entries,
    [property: JsonPropertyName("subclassTitle")]         string? SubclassTitle);
