using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed partial class OllamaLlmEntityExtractor(
    IOllamaApiClient ollama,
    IOptions<OllamaOptions> options,
    IOptions<IngestionOptions> ingestionOptions,
    ILogger<OllamaLlmEntityExtractor> logger) : ILlmEntityExtractor
{
    private static readonly Dictionary<string, string> TypeFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Spell"]       = "level (int), school (string), casting_time (string), range (string), components (string), duration (string), description (string)",
        ["Monster"]     = "size (string), type (string), alignment (string), ac (int), hp (string), speed (string), abilities (object: str/dex/con/int/wis/cha), description (string)",
        ["Class"]       = "hit_die (string), primary_ability (string), saving_throws (string[]), armor_proficiencies (string), weapon_proficiencies (string), features (array of {level, name, description})",
        ["Race"]        = "description (string)",
        ["Background"]  = "description (string)",
        ["Item"]        = "description (string)",
        ["Rule"]        = "description (string)",
        ["Combat"]      = "description (string)",
        ["Adventuring"] = "description (string)",
        ["Condition"]   = "description (string)",
        ["God"]         = "description (string)",
        ["Plane"]       = "description (string)",
        ["Treasure"]    = "description (string)",
        ["Encounter"]   = "description (string)",
        ["Trap"]        = "description (string)",
        ["Trait"]       = "description (string), source_category (string)",
        ["Lore"]        = "description (string)",
    };

    private readonly string _model = options.Value.ExtractionModel;
    private readonly int _numCtx = options.Value.ExtractionNumCtx;
    private readonly int _timeoutSeconds = options.Value.ExtractionTimeoutSeconds;

    public async Task<IReadOnlyList<ExtractedEntity>> ExtractAsync(
        string pageText,
        string entityType,
        int pageNumber,
        string sourceBook,
        string version,
        string entityName,
        int sectionStartPage,
        int sectionEndPage,
        CancellationToken ct = default)
    {
        LogExtractStart(logger, entityType, pageNumber, _model);
        var sw = Stopwatch.StartNew();

        var fields = TypeFields.GetValueOrDefault(entityType, "description (string)");
        var systemPrompt =
            $"""
            You are a D&D 5e content extractor. Extract all {entityType} entities from the section text below.
            This is a section from the {entityName} {entityType} (pages {sectionStartPage}–{sectionEndPage}).

            OUTPUT RULES — follow exactly:
            1. Reply with a JSON ARRAY and nothing else. No object wrapper, no prose, no section headings as keys.
            2. Each array element must have exactly these keys: "name" (string), "partial" (bool), "data" (object).
            3. "partial" is true only if the entity is cut off at the page boundary.
            4. Use null for any missing fields inside "data".
            5. If there are no {entityType} entities on this page, return exactly: []

            Fields for {entityType}: {fields}
            """;

        var request = new ChatRequest
        {
            Model = _model,
            Stream = true,
            Format = "json",
            Options = new OllamaSharp.Models.RequestOptions { NumCtx = _numCtx },
            Messages =
            [
                new Message { Role = ChatRole.System, Content = systemPrompt },
                new Message { Role = ChatRole.User, Content = pageText }
            ]
        };

        var maxAttempts = ingestionOptions.Value.LlmExtractionRetries + 1;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var sb = new StringBuilder();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));
            try
            {
                await foreach (var chunk in ollama.ChatAsync(request, timeoutCts.Token))
                    sb.Append(chunk?.Message?.Content ?? string.Empty);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                LogExtractionTimeout(logger, entityType, pageNumber, _timeoutSeconds);
                return [];
            }

            var json = StripFences(sb.ToString().Trim());

            if (string.IsNullOrEmpty(json))
            {
                if (attempt < maxAttempts)
                {
                    LogRetryExtraction(logger, entityType, pageNumber, attempt, maxAttempts);
                    continue;
                }
                LogExtractDone(logger, entityType, pageNumber, 0, _model, sw.ElapsedMilliseconds);
                return [];
            }

            try
            {
                var raw = JsonNode.Parse(json)?.AsArray();
                if (raw is null)
                {
                    if (attempt < maxAttempts)
                    {
                        LogRetryExtraction(logger, entityType, pageNumber, attempt, maxAttempts);
                        continue;
                    }
                    LogInvalidJson(logger, entityType, pageNumber, json[..Math.Min(200, json.Length)]);
                    return [];
                }

                var results = new List<ExtractedEntity>();
                foreach (var item in raw)
                {
                    if (item is not JsonObject obj) continue;
                    var name = obj["name"]?.GetValue<string>() ?? string.Empty;
                    var partial = obj["partial"]?.GetValue<bool>() ?? false;
                    var data = obj["data"]?.AsObject() ?? new JsonObject();
                    results.Add(new ExtractedEntity(pageNumber, sourceBook, version, partial, entityType, name, data));
                }
                LogExtractDone(logger, entityType, pageNumber, results.Count, _model, sw.ElapsedMilliseconds);
                return results;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                if (attempt < maxAttempts)
                {
                    LogRetryExtraction(logger, entityType, pageNumber, attempt, maxAttempts);
                }
                else
                {
                    LogInvalidJson(logger, entityType, pageNumber, json[..Math.Min(200, json.Length)]);
                    return [];
                }
            }
        }
        // unreachable but compiler needs it
        return [];
    }

    private static string StripFences(string s)
    {
        if (s.StartsWith("```", StringComparison.Ordinal))
        {
            var start = s.IndexOf('\n') + 1;
            var end = s.LastIndexOf("```", StringComparison.Ordinal);
            if (end > start) s = s[start..end].Trim();
        }

        try
        {
            var node = JsonNode.Parse(s);
            if (node is JsonObject obj)
            {
                // {"entities":[...]} or {"items":[...]} — single-key array wrapper
                if (obj.Count == 1)
                {
                    var inner = obj.First().Value;
                    if (inner is JsonArray) return inner.ToJsonString();
                }

                // {"name":"...","partial":...,"data":{...}} — single entity instead of array
                if (obj.ContainsKey("name") && obj.ContainsKey("partial") && obj.ContainsKey("data"))
                    return new JsonArray(obj.DeepClone()).ToJsonString();

                // {"Section Heading": {"name":..., "partial":..., "data":{...}}, ...}
                // Model used headings as keys instead of array elements — convert values to array.
                if (obj.All(kv => kv.Value is JsonObject))
                    return new JsonArray(obj.Select(kv => kv.Value?.DeepClone()).ToArray()).ToJsonString();
            }
        }
        catch { /* not valid JSON yet — return as-is and let the caller handle it */ }

        return s;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Extracting {EntityType} from page {Page} with {Model}")]
    private static partial void LogExtractStart(ILogger logger, string entityType, int page, string model);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Extracted {Count} {EntityType} from page {Page} with {Model} in {ElapsedMs}ms")]
    private static partial void LogExtractDone(ILogger logger, string entityType, int page, int count, string model, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Extractor returned invalid JSON for type={Type} page={Page}: {Json}")]
    private static partial void LogInvalidJson(ILogger logger, string type, int page, string json);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Retrying extraction for {EntityType} page {Page} (attempt {Attempt}/{Max})")]
    private static partial void LogRetryExtraction(ILogger logger, string entityType, int page, int attempt, int max);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Extraction timed out for {EntityType} page {Page} after {TimeoutSeconds}s — skipping")]
    private static partial void LogExtractionTimeout(ILogger logger, string entityType, int page, int timeoutSeconds);
}
