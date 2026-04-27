using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;

namespace DndMcpAICsharpFun.Features.Embedding;

public sealed class OllamaEmbeddingService : IEmbeddingService
{
    private readonly OllamaApiClient _client;
    private readonly string _model;

    public OllamaEmbeddingService(OllamaApiClient client, IOptions<OllamaOptions> options)
    {
        _client = client;
        _model = options.Value.EmbeddingModel;
    }

    public async Task<IList<float[]>> EmbedAsync(IList<string> texts, CancellationToken ct = default)
    {
        try
        {
            var request = new EmbedRequest
            {
                Model = _model,
                Input = [.. texts],
                Options = new OllamaSharp.Models.RequestOptions { NumCtx = 2048 },
                Truncate = true
            };
            var response = await _client.EmbedAsync(request, ct);
            return response.Embeddings;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Ollama embedding request failed (model: {_model}): {ex.Message}", ex);
        }
    }
}
