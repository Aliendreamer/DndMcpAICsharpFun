using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
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
    /// Content-first extraction: issues ONE call constrained by the discriminated-union schema and
    /// returns the raw response object (which carries the model-selected <c>entityType</c> branch or
    /// the <c>none</c> decline). Type selection needs a single call over the candidate text, so the
    /// first chunk is used when the text is long (no per-chunk type voting).
    /// </summary>
    public async Task<UnionExtraction> ExtractUnionAsync(
        DndMcpAICsharpFun.Domain.IngestionRecord record,
        EntityCandidate candidate,
        IReadOnlyList<EntityType> prior,
        IReadOnlyDictionary<EntityType, JsonElement> schemas,
        CancellationToken ct)
    {
        var union = ExtractionUnionSchemaBuilder.Build(prior, schemas);
        var chunks = chunker.Split(candidate.Text, _opts.MaxTokensPerChunk);
        if (chunks.Count == 0) chunks = new List<string> { candidate.Text };

        // Type decision: the union call sees the WHOLE candidate (not just chunks[0]) so it can
        // recognise a stat block even when the entry opens with a lore intro. Ollama truncates the
        // input to the model context (keeping the top, where the stat block sits) for long entries;
        // remaining chunks are still completed per-type below.
        var firstResp = await CallAsync(
            promptBuilder.BuildUnionSystemPrompt(record.DisplayName, record.Version),
            candidate,
            "emit_entity", "Emit one entity branch or decline via entityType:none.",
            union, ct);

        if (!firstResp.Success || firstResp.ToolInput is null)
            return UnionExtraction.Failure(firstResp.ErrorMessage ?? "union extraction failed");

        var root = firstResp.ToolInput.Value;
        var entityType = root.TryGetProperty("entityType", out var et) ? et.GetString() : null;
        if (string.Equals(entityType, ExtractionUnionSchemaBuilder.DeclineType, StringComparison.Ordinal))
        {
            var reason = root.TryGetProperty("reason", out var rp) ? rp.GetString() : null;
            return UnionExtraction.Decline(reason);
        }

        var confidence = root.TryGetProperty("confidence", out var cp) ? cp.GetString() : null;
        var selectedType = Enum.TryParse<EntityType>(entityType, out var parsed) ? parsed : candidate.Type;
        var partials = new List<JsonElement> { StripBranchEnvelope(root) };

        // Remaining chunks: complete fields using the selected type's per-type schema, then merge,
        // so long entities are not truncated to their first chunk.
        if (chunks.Count > 1 && schemas.TryGetValue(selectedType, out var typeSchema))
        {
            for (var c = 1; c < chunks.Count; c++)
            {
                var resp = await CallAsync(
                    promptBuilder.BuildSystemPrompt(record.DisplayName, record.Version, selectedType),
                    candidate with { Text = chunks[c], Type = selectedType },
                    promptBuilder.ToolName(selectedType), promptBuilder.ToolDescription(selectedType),
                    typeSchema, ct);
                if (resp.Success && resp.ToolInput is not null)
                    partials.Add(StripConfidence(resp.ToolInput.Value));
            }
        }

        var merged = partials.Count == 1 ? partials[0] : merger.Merge(partials);
        return UnionExtraction.Typed(selectedType, merged, confidence);
    }

    private async Task<ExtractionResponse> CallAsync(
        string systemPrompt, EntityCandidate candidate, string toolName, string toolDescription,
        JsonElement schema, CancellationToken ct)
    {
        var request = new ExtractionRequest(
            SystemPrompt:    systemPrompt,
            UserPrompt:      promptBuilder.BuildUserPrompt(candidate),
            ToolName:        toolName,
            ToolDescription: toolDescription,
            ToolInputSchema: schema,
            ModelId:         _ollama.ChatModel,
            MaxOutputTokens: _opts.MaxOutputTokensPerEntity);

        return await retry.ExecuteAsync(
            operation: (_, c2) => llm.ExtractAsync(request, c2),
            isSuccess: r => r.Success,
            ct);
    }

    /// <summary>
    /// Returns a copy of a union branch object with the discriminator (<c>entityType</c>) and the
    /// synthetic <c>confidence</c> property removed, leaving just the entity's fields.
    /// </summary>
    public static JsonElement StripBranchEnvelope(JsonElement branch)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            foreach (var prop in branch.EnumerateObject())
                if (!string.Equals(prop.Name, "entityType", StringComparison.Ordinal) &&
                    !string.Equals(prop.Name, "confidence", StringComparison.Ordinal))
                    prop.WriteTo(writer);
            writer.WriteEndObject();
        }
        using var doc = JsonDocument.Parse(ms.ToArray());
        return doc.RootElement.Clone();
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
