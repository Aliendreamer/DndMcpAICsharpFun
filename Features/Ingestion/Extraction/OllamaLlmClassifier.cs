using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed partial class OllamaLlmClassifier(
    IOllamaApiClient ollama,
    IOptions<OllamaOptions> options,
    ILogger<OllamaLlmClassifier> logger) : ILlmClassifier
{
    private const string SystemPrompt =
        """
        You are a D&D 5e content classifier. Given a page of text from a D&D rulebook,
        identify which entity types are present. Reply with a JSON object:
        {"types": ["Spell", "Monster"]}
        Use only these values: Spell, Monster, Class, Background, Item, Rule, Treasure,
        Encounter, Trap. Use an empty array if nothing matches: {"types": []}.
        Reply with JSON only, no explanation.
        """;

    private readonly string _model = options.Value.ExtractionModel;

    public async Task<IReadOnlyList<string>> ClassifyPageAsync(string pageText, CancellationToken ct = default)
    {
        LogClassifyStart(logger, _model, pageText.Length);
        var sw = Stopwatch.StartNew();

        var request = new ChatRequest
        {
            Model = _model,
            Stream = true,
            Format = "json",
            Messages =
            [
                new Message { Role = ChatRole.System, Content = SystemPrompt },
                new Message { Role = ChatRole.User, Content = pageText }
            ]
        };

        var sb = new StringBuilder();
        await foreach (var chunk in ollama.ChatAsync(request, ct))
            sb.Append(chunk?.Message?.Content ?? string.Empty);

        var json = StripFences(sb.ToString().Trim());

        if (string.IsNullOrEmpty(json))
        {
            LogClassifyDone(logger, _model, "none", sw.ElapsedMilliseconds);
            return [];
        }

        try
        {
            var result = ParseTypes(json);
            LogClassifyDone(logger, _model, result.Count > 0 ? string.Join(",", result) : "none", sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            LogInvalidJson(logger, json[..Math.Min(200, json.Length)]);
            LogClassifyDone(logger, _model, "none", sw.ElapsedMilliseconds);
            return [];
        }
    }

    private static List<string> ParseTypes(string json)
    {
        var node = JsonNode.Parse(json);

        // {"types": [...]} — expected object shape
        if (node is JsonObject obj)
        {
            var arr = obj["types"]?.AsArray();
            if (arr is null) return [];
            return [.. arr.Select(n => n?.GetValue<string>() ?? string.Empty).Where(s => s.Length > 0)];
        }

        // ["Spell", ...] — model returned a bare array anyway
        if (node is JsonArray bare)
            return [.. bare.Select(n => n?.GetValue<string>() ?? string.Empty).Where(s => s.Length > 0)];

        return [];
    }

    private static string StripFences(string s)
    {
        if (s.StartsWith("```", StringComparison.Ordinal))
        {
            var start = s.IndexOf('\n') + 1;
            var end = s.LastIndexOf("```", StringComparison.Ordinal);
            if (end > start) return s[start..end].Trim();
        }
        return s;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Classifying {TextLength} chars with {Model}")]
    private static partial void LogClassifyStart(ILogger logger, string model, int textLength);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Classified with {Model} → {Categories} in {ElapsedMs}ms")]
    private static partial void LogClassifyDone(ILogger logger, string model, string categories, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Classifier returned invalid JSON: {Json}")]
    private static partial void LogInvalidJson(ILogger logger, string json);
}
