// This class is scheduled for deletion in Task 2 (replaced by OllamaTocMapExtractor).
// Trait and Lore categories are intentionally not in the prompt here.
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

        Valid categories: Spell, Monster, Class, Race, Background, Item, Rule, Combat, Adventuring, Condition, God, Plane, Treasure, Encounter, Trap

        Mapping guidance:
        - "Spells" or "Spell Descriptions" → Spell
        - "Monsters", "Bestiary", "Creature Statistics" → Monster
        - "Classes", "Class Features" → Class
        - "Races", "Species" → Race
        - "Backgrounds" → Background
        - "Equipment", "Gear", "Items", "Magic Items" → Item
        - "Combat", "Actions in Combat", "Order of Combat" → Combat
        - "Adventuring", "Between Adventures", "Resting" → Adventuring
        - "Conditions" → Condition
        - "Gods", "Deities", "Pantheon", "Religion" → God
        - "Planes", "The Planes of Existence", "Cosmology" → Plane
        - "Treasure" → Treasure
        - "Encounters", "Random Encounters" → Encounter
        - "Traps" → Trap
        - Ability scores, skills, saving throws, proficiency → Rule
        - Introduction, Preface, Index, Table of Contents, Character Sheet, Inspirational Reading → null

        Use Rule as the catch-all for any game content that does not fit a specific category above.
        Use null only for non-game content (front matter, index, appendices with no game rules).

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

            var ranges = new List<TocSectionEntry>();
            foreach (var node in array)
            {
                if (node is not JsonObject obj) continue;
                var startPage = obj["startPage"]?.GetValue<int>() ?? 0;
                var titleStr = obj["title"]?.GetValue<string>() ?? string.Empty;
                var categoryStr = obj["category"]?.GetValue<string>();
                ContentCategory? category = Enum.TryParse<ContentCategory>(categoryStr, out var c) ? c : null;
                ranges.Add(new TocSectionEntry(titleStr, category, startPage));
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
