using System.Text.Json;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Wraps the LLM tool-call extraction for a single <see cref="EntityCandidate"/>:
/// splits the candidate text into chunks, calls the Ollama extraction client per chunk,
/// merges partial results, and strips the synthetic <c>confidence</c> field.
/// </summary>
public sealed class CandidateExtractor(
    IEntityExtractionLlmClient llm,
    ExtractionPromptBuilder promptBuilder,
    SemanticChunker chunker,
    EntityFieldMerger merger,
    ExtractionRetryPolicy retry,
    IOptions<EntityExtractionOptions> options,
    IOptions<OllamaOptions> ollamaOpts,
    ILogger<CandidateExtractor> logger)
{
    private readonly EntityExtractionOptions _opts   = options.Value;
    private readonly OllamaOptions           _ollama = ollamaOpts.Value;

    public async Task<(JsonElement? Fields, string? ErrorMessage)> ExtractFieldsAsync(
        DndMcpAICsharpFun.Domain.IngestionRecord record,
        EntityCandidate candidate,
        JsonElement schema,
        CancellationToken ct)
    {
        var chunks        = chunker.Split(candidate.Text, _opts.MaxTokensPerChunk);
        var partials      = new List<JsonElement>();
        var chunkFailures = new List<int>();
        string? lastError = null;

        for (int c = 0; c < chunks.Count; c++)
        {
            var chunkCandidate = chunks.Count == 1 ? candidate : candidate with { Text = chunks[c] };
            var request = new ExtractionRequest(
                SystemPrompt:    promptBuilder.BuildSystemPrompt(record.DisplayName, record.Version, candidate.Type),
                UserPrompt:      promptBuilder.BuildUserPrompt(chunkCandidate),
                ToolName:        promptBuilder.ToolName(candidate.Type),
                ToolDescription: promptBuilder.ToolDescription(candidate.Type),
                ToolInputSchema: schema,
                ModelId:         _ollama.ChatModel,
                MaxOutputTokens: _opts.MaxOutputTokensPerEntity);

            var response = await retry.ExecuteAsync(
                operation: (_, c2) => llm.ExtractAsync(request, c2),
                isSuccess: r => r.Success,
                ct);

            if (response.Success && response.ToolInput is not null)
            {
                partials.Add(response.ToolInput.Value);
            }
            else
            {
                chunkFailures.Add(c);
                lastError = response.ErrorMessage;
            }
        }

        if (partials.Count == 0)
            return (null, lastError ?? "all chunks failed");

        if (chunkFailures.Count > 0)
            logger.LogWarning(
                "Partial extraction for {Type} '{Name}': chunks [{Failed}] failed, {Ok}/{Total} ok",
                candidate.Type, candidate.DisplayName,
                string.Join(',', chunkFailures), partials.Count, chunks.Count);

        var merged = partials.Count == 1 ? partials[0] : merger.Merge(partials);
        return (merged, null);
    }

    /// <summary>
    /// Returns a copy of <paramref name="toolInput"/> with the <c>confidence</c> property removed.
    /// </summary>
    public static JsonElement StripConfidence(JsonElement toolInput)
    {
        using var ms     = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);
        writer.WriteStartObject();
        foreach (var prop in toolInput.EnumerateObject())
            if (!string.Equals(prop.Name, "confidence", StringComparison.Ordinal))
                prop.WriteTo(writer);
        writer.WriteEndObject();
        writer.Flush();
        using var doc = JsonDocument.Parse(ms.ToArray());
        return doc.RootElement.Clone();
    }
}
