using System.Text.Json;
using System.Text.Json.Serialization;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Single source-of-truth for canonical JSON (de)serialization settings.
/// All writers of canonical *.json files share these options so that
/// enum values are consistently written as strings and formatting is uniform.
/// </summary>
public static class CanonicalJson
{
    public static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
