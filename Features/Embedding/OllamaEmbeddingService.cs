using System.Diagnostics;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;

namespace DndMcpAICsharpFun.Features.Embedding;

public sealed partial class OllamaEmbeddingService(
    IOllamaApiClient client,
    IOptions<OllamaOptions> options,
    ILogger<OllamaEmbeddingService> logger) : IEmbeddingService
{
    private const int MaxEmbedChars = 1500;

    private readonly string _model = options.Value.EmbeddingModel;

    public async Task<IList<float[]>> EmbedAsync(IList<string> texts, CancellationToken ct = default)
    {
        LogEmbedBatchStart(logger, texts.Count, _model);
        var sw = Stopwatch.StartNew();

        var prepared = new string[texts.Count];
        var truncated = 0;
        for (var i = 0; i < texts.Count; i++)
        {
            var t = texts[i] ?? string.Empty;
            if (t.Length > MaxEmbedChars)
            {
                prepared[i] = t[..MaxEmbedChars];
                truncated++;
            }
            else
            {
                prepared[i] = t;
            }
        }
        if (truncated > 0)
            LogTruncated(logger, truncated, texts.Count, MaxEmbedChars);

        try
        {
            var request = new EmbedRequest
            {
                Model = _model,
                Input = [.. prepared],
                Truncate = true
            };
            var response = await client.EmbedAsync(request, ct);
            LogEmbedBatchDone(logger, texts.Count, _model, sw.ElapsedMilliseconds);
            return response.Embeddings;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Ollama embedding request failed (model: {_model}): {ex.Message}", ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Embedding {ChunkCount} chunks with {Model}")]
    private static partial void LogEmbedBatchStart(ILogger logger, int chunkCount, string model);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Embedded {ChunkCount} chunks with {Model} in {ElapsedMs}ms")]
    private static partial void LogEmbedBatchDone(ILogger logger, int chunkCount, string model, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Truncated {Count}/{Total} inputs to {MaxChars} chars before embedding")]
    private static partial void LogTruncated(ILogger logger, int count, int total, int maxChars);
}
