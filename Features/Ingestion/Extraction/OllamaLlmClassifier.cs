using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed partial class OllamaLlmClassifier(
    OllamaApiClient ollama,
    IOptions<OllamaOptions> options,
    ILogger<OllamaLlmClassifier> logger) : ILlmClassifier
{
    private const string SystemPrompt =
        """
        You are a D&D 5e content classifier. Given a page of text from a D&D rulebook,
        list only the entity types present on this page. Reply with a JSON array of strings
        using only these values: Spell, Monster, Class, Background, Item, Rule, Treasure,
        Encounter, Trap. Reply with [] if no entities are found. Reply with JSON only, no
        explanation.
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
            var result = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            LogClassifyDone(logger, _model, result.Count > 0 ? string.Join(",", result) : "none", sw.ElapsedMilliseconds);
            return result;
        }
        catch (JsonException)
        {
            LogInvalidJson(logger, json[..Math.Min(200, json.Length)]);
            LogClassifyDone(logger, _model, "none", sw.ElapsedMilliseconds);
            return [];
        }
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
