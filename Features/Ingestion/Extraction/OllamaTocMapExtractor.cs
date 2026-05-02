using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed partial class OllamaTocMapExtractor(
    IOllamaApiClient ollama,
    IOptions<OllamaOptions> options,
    ILogger<OllamaTocMapExtractor> logger) : ITocMapExtractor
{
    private const string SystemPrompt =
        """
        You are a D&D rulebook table-of-contents parser.
        Given raw TOC page text, extract all chapter or section entries.

        Return a JSON array. Each element must have exactly these keys:
        - "title": string (chapter or section title)
        - "category": one of Spell, Monster, Class, Race, Background, Item, Rule, Combat,
          Adventuring, Condition, God, Plane, Treasure, Encounter, Trap, Trait, Lore, or null
        - "startPage": integer (page number where this section begins)
        - "endPage": integer or null (page where this section ends, null if not stated)

        Category guidance:
        "Spells"/"Spell Descriptions" -> Spell
        "Monsters"/"Bestiary" -> Monster
        "Classes"/"Class Features" -> Class
        "Races"/"Species" -> Race
        "Backgrounds" -> Background
        "Equipment"/"Items"/"Magic Items" -> Item
        "Combat"/"Actions in Combat" -> Combat
        "Adventuring"/"Resting" -> Adventuring
        "Conditions" -> Condition
        "Gods"/"Deities"/"Pantheon" -> God
        "Planes"/"Cosmology" -> Plane
        "Treasure" -> Treasure
        "Encounters" -> Encounter
        "Traps" -> Trap
        "Traits"/"Character Traits" -> Trait
        "Lore"/"History"/"World Background" -> Lore
        Introduction, Preface, Index, Appendix, Contents -> null
        Ability scores, skills, proficiency, rules -> Rule

        Reply with JSON only, no explanation.
        """;

    private readonly string _model = options.Value.ExtractionModel;
    private readonly int _numCtx = options.Value.ExtractionNumCtx;

    public async Task<IReadOnlyList<TocSectionEntry>> ExtractMapAsync(
        string tocPageText,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tocPageText))
            return [];

        var request = new ChatRequest
        {
            Model = _model,
            Stream = true,
            Format = "json",
            Options = new OllamaSharp.Models.RequestOptions { NumCtx = _numCtx },
            Messages =
            [
                new Message { Role = ChatRole.System, Content = SystemPrompt },
                new Message { Role = ChatRole.User, Content = tocPageText }
            ]
        };

        var sb = new StringBuilder();
        await foreach (var chunk in ollama.ChatAsync(request, ct))
            sb.Append(chunk?.Message?.Content ?? string.Empty);

        var json = sb.ToString().Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var start = json.IndexOf('\n') + 1;
            var end = json.LastIndexOf("```", StringComparison.Ordinal);
            if (end > start) json = json[start..end].Trim();
        }

        try
        {
            var array = JsonNode.Parse(json)?.AsArray();
            if (array is null)
            {
                LogInvalidJson(logger, json[..Math.Min(200, json.Length)]);
                return [];
            }

            var entries = new List<TocSectionEntry>(array.Count);
            foreach (var node in array)
            {
                if (node is not JsonObject obj) continue;

                var title = obj["title"]?.GetValue<string>() ?? string.Empty;
                var categoryStr = obj["category"]?.GetValue<string>();
                ContentCategory? category = Enum.TryParse<ContentCategory>(categoryStr, out var c) ? c : null;
                var startPage = obj["startPage"]?.GetValue<int>() ?? 0;

                int? endPage = null;
                if (obj["endPage"] is JsonValue endVal && endVal.TryGetValue<int>(out var ep))
                    endPage = ep;

                if (startPage > 0)
                    entries.Add(new TocSectionEntry(title, category, startPage, endPage));
            }

            LogExtracted(logger, entries.Count, _model);
            return entries;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            LogInvalidJson(logger, json[..Math.Min(200, json.Length)]);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "TOC map extractor returned invalid JSON: {Json}")]
    private static partial void LogInvalidJson(ILogger logger, string json);

    [LoggerMessage(Level = LogLevel.Information, Message = "TOC map extracted {Count} entries with {Model}")]
    private static partial void LogExtracted(ILogger logger, int count, string model);
}
