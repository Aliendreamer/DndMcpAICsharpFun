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
        var request = new ChatRequest
        {
            Model = _model,
            Stream = false,
            Messages =
            [
                new Message { Role = ChatRole.System, Content = SystemPrompt },
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
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            LogInvalidJson(logger, json[..Math.Min(200, json.Length)]);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Classifier returned invalid JSON: {Json}")]
    private static partial void LogInvalidJson(ILogger logger, string json);
}
