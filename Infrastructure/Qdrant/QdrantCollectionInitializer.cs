using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Infrastructure.Qdrant;

public sealed partial class QdrantCollectionInitializer(
    QdrantClient client,
    IOptions<QdrantOptions> options,
    ILogger<QdrantCollectionInitializer> logger) : IHostedService
{
    private readonly QdrantOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var exists = await client.CollectionExistsAsync(_options.CollectionName, cancellationToken);
            if (exists)
            {
                LogCollectionExists(logger, _options.CollectionName);
                return;
            }

            await client.CreateCollectionAsync(
                _options.CollectionName,
                new VectorParams { Size = (ulong)_options.VectorSize, Distance = Distance.Cosine },
                cancellationToken: cancellationToken);

            LogCollectionCreated(logger, _options.CollectionName, _options.VectorSize);

            await CreatePayloadIndexesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            LogCollectionInitFailed(logger, ex, _options.CollectionName);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CreatePayloadIndexesAsync(CancellationToken ct)
    {
        string[] keywordFields = ["source_book", "version", "category", "entity_name"];
        foreach (var field in keywordFields)
            await client.CreatePayloadIndexAsync(_options.CollectionName, field, PayloadSchemaType.Keyword, cancellationToken: ct);

        string[] intFields = ["page_number", "chunk_index"];
        foreach (var field in intFields)
            await client.CreatePayloadIndexAsync(_options.CollectionName, field, PayloadSchemaType.Integer, cancellationToken: ct);

        LogPayloadIndexesCreated(logger, _options.CollectionName);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Qdrant collection '{Collection}' already exists")]
    private static partial void LogCollectionExists(ILogger logger, string collection);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created Qdrant collection '{Collection}' (size={Size}, distance=Cosine)")]
    private static partial void LogCollectionCreated(ILogger logger, string collection, int size);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to initialise Qdrant collection '{Collection}'")]
    private static partial void LogCollectionInitFailed(ILogger logger, Exception ex, string collection);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created payload indexes on collection '{Collection}'")]
    private static partial void LogPayloadIndexesCreated(ILogger logger, string collection);
}
