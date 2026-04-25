using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Infrastructure.Qdrant;

public sealed class QdrantCollectionInitializer : IHostedService
{
    private readonly QdrantClient _client;
    private readonly QdrantOptions _options;
    private readonly ILogger<QdrantCollectionInitializer> _logger;

    public QdrantCollectionInitializer(
        QdrantClient client,
        IOptions<QdrantOptions> options,
        ILogger<QdrantCollectionInitializer> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var exists = await _client.CollectionExistsAsync(_options.CollectionName, cancellationToken);
            if (exists)
            {
                _logger.LogInformation("Qdrant collection '{Collection}' already exists", _options.CollectionName);
                return;
            }

            await _client.CreateCollectionAsync(
                _options.CollectionName,
                new VectorParams { Size = (ulong)_options.VectorSize, Distance = Distance.Cosine },
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Created Qdrant collection '{Collection}' (size={Size}, distance=Cosine)",
                _options.CollectionName, _options.VectorSize);

            await CreatePayloadIndexesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialise Qdrant collection '{Collection}'", _options.CollectionName);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CreatePayloadIndexesAsync(CancellationToken ct)
    {
        var keywordFields = new[] { "source_book", "version", "category", "entity_name" };
        foreach (var field in keywordFields)
            await _client.CreatePayloadIndexAsync(_options.CollectionName, field, PayloadSchemaType.Keyword, cancellationToken: ct);

        var intFields = new[] { "page_number", "chunk_index" };
        foreach (var field in intFields)
            await _client.CreatePayloadIndexAsync(_options.CollectionName, field, PayloadSchemaType.Integer, cancellationToken: ct);

        _logger.LogInformation("Created payload indexes on collection '{Collection}'", _options.CollectionName);
    }
}
