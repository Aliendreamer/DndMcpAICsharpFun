using System.Diagnostics;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;

namespace DndMcpAICsharpFun.Features.Embedding;

public sealed partial class OllamaEmbeddingService(
    OllamaApiClient client,
    IOptions<OllamaOptions> options,
    ILogger<OllamaEmbeddingService> logger) : IEmbeddingService
{
    private readonly string _model = options.Value.EmbeddingModel;

    public async Task<IList<float[]>> EmbedAsync(IList<string> texts, CancellationToken ct = default)
    {
        LogEmbedBatchStart(logger, texts.Count, _model);
        var sw = Stopwatch.StartNew();

        try
        {
            var request = new EmbedRequest
            {
                Model = _model,
                Input = [.. texts],
                Options = new OllamaSharp.Models.RequestOptions { NumCtx = 2048 },
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
}
