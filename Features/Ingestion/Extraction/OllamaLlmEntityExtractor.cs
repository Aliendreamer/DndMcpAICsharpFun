using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed partial class OllamaLlmEntityExtractor(
    OllamaApiClient ollama,
    IOptions<OllamaOptions> options,
    ILogger<OllamaLlmEntityExtractor> logger) : ILlmEntityExtractor
{
    private static readonly Dictionary<string, string> TypeFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Spell"]      = "level (int), school (string), casting_time (string), range (string), components (string), duration (string), description (string)",
        ["Monster"]    = "size (string), type (string), alignment (string), ac (int), hp (string), speed (string), abilities (object: str/dex/con/int/wis/cha), description (string)",
        ["Class"]      = "hit_die (string), primary_ability (string), saving_throws (string[]), armor_proficiencies (string), weapon_proficiencies (string), features (array of {level, name, description})",
        ["Background"] = "description (string)",
        ["Item"]       = "description (string)",
        ["Rule"]       = "description (string)",
        ["Treasure"]   = "description (string)",
        ["Encounter"]  = "description (string)",
        ["Trap"]       = "description (string)",
    };

    private readonly string _model = options.Value.ExtractionModel;

    public async Task<IReadOnlyList<ExtractedEntity>> ExtractAsync(
        string pageText,
        string entityType,
        int pageNumber,
        string sourceBook,
        string version,
        CancellationToken ct = default)
    {
        var fields = TypeFields.GetValueOrDefault(entityType, "description (string)");
        var systemPrompt =
            $"""
            You are a D&D 5e content extractor. Extract all {entityType} entities from the page text
            below. Return a JSON array of objects. Each object must have:
            - name (string)
            - partial (bool — true if the entity appears cut off at the page boundary)
            - data (object with the fields listed below)

            Use null for any missing fields. Reply with JSON only, no explanation.

            Fields for {entityType}: {fields}
            """;

        var request = new ChatRequest
        {
            Model = _model,
            Stream = true,
            Messages =
            [
                new Message { Role = ChatRole.System, Content = systemPrompt },
                new Message { Role = ChatRole.User, Content = pageText }
            ]
        };

        var sb = new StringBuilder();
        await foreach (var chunk in ollama.ChatAsync(request, ct))
            sb.Append(chunk?.Message?.Content ?? string.Empty);

        var json = sb.ToString().Trim();

        if (string.IsNullOrEmpty(json))
            return [];

        try
        {
            var raw = JsonNode.Parse(json)?.AsArray();
            if (raw is null) return [];

            var results = new List<ExtractedEntity>();
            foreach (var item in raw)
            {
                if (item is not JsonObject obj) continue;
                var name = obj["name"]?.GetValue<string>() ?? string.Empty;
                var partial = obj["partial"]?.GetValue<bool>() ?? false;
                var data = obj["data"]?.AsObject() ?? new JsonObject();

                results.Add(new ExtractedEntity(pageNumber, sourceBook, version, partial, entityType, name, data));
            }
            return results;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            LogInvalidJson(logger, entityType, pageNumber, json[..Math.Min(200, json.Length)]);

            // Fallback: save raw page text as a Rule entity with partial:true — nothing silently dropped
            var fallbackData = new JsonObject { ["description"] = pageText };
            return [new ExtractedEntity(pageNumber, sourceBook, version, true, "Rule", $"page_{pageNumber}_raw", fallbackData)];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Extractor returned invalid JSON for type={Type} page={Page}: {Json}")]
    private static partial void LogInvalidJson(ILogger logger, string type, int page, string json);
}
