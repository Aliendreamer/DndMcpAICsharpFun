using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed partial class OllamaTocCategoryClassifier(
    IOllamaApiClient ollama,
    IOptions<OllamaOptions> options,
    ILogger<OllamaTocCategoryClassifier> logger) : ITocCategoryClassifier
{
    private const string SystemPrompt =
        """
        You are a D&D rulebook chapter classifier.
        Given a list of PDF chapter titles and their starting page numbers, determine which
        D&D content category best matches each chapter.

        Valid categories: Spell, Monster, Class, Background, Item, Rule, Treasure, Encounter, Trap
        Use null if no category applies (e.g. Introduction, Preface, Index, Table of Contents).

        Return a JSON array, one entry per bookmark in the same order:
        [{"startPage": N, "category": "Spell"|null}, ...]

        Reply with JSON only, no explanation.
        """;

    private readonly string _model = options.Value.ExtractionModel;

    public async Task<TocCategoryMap> ClassifyAsync(
        IReadOnlyList<PdfBookmark> bookmarks,
        CancellationToken ct = default)
    {
        if (bookmarks.Count == 0)
            return new TocCategoryMap([]);

        var input = JsonSerializer.Serialize(
            bookmarks.Select(static b => new { title = b.Title, startPage = b.PageNumber }));

        var request = new ChatRequest
        {
            Model = _model,
            Stream = true,
            Format = "json",
            Messages =
            [
                new Message { Role = ChatRole.System, Content = SystemPrompt },
                new Message { Role = ChatRole.User, Content = input }
            ]
        };

        var sb = new StringBuilder();
        await foreach (var chunk in ollama.ChatAsync(request, ct))
            sb.Append(chunk?.Message?.Content ?? string.Empty);

        var json = sb.ToString().Trim();

        try
        {
            var array = JsonNode.Parse(json)?.AsArray();
            if (array is null) return FallbackMap(json);

            var ranges = new List<(int, ContentCategory?)>();
            foreach (var node in array)
            {
                if (node is not JsonObject obj) continue;
                var startPage = obj["startPage"]?.GetValue<int>() ?? 0;
                var categoryStr = obj["category"]?.GetValue<string>();
                ContentCategory? category = Enum.TryParse<ContentCategory>(categoryStr, out var c) ? c : null;
                ranges.Add((startPage, category));
            }

            LogClassified(logger, ranges.Count, _model);
            return new TocCategoryMap(ranges);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return FallbackMap(json);
        }
    }

    private TocCategoryMap FallbackMap(string json)
    {
        LogInvalidJson(logger, json[..Math.Min(200, json.Length)]);
        return new TocCategoryMap([]);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "TOC classified {Count} ranges with {Model}")]
    private static partial void LogClassified(ILogger logger, int count, string model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TOC classifier returned invalid JSON: {Json} — falling back to all-categories")]
    private static partial void LogInvalidJson(ILogger logger, string json);
}
