using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Infrastructure.Qdrant;

[ExcludeFromCodeCoverage]
public sealed partial class QdrantCollectionInitializer(
    QdrantClient client,
    IOptions<QdrantOptions> options,
    ILogger<QdrantCollectionInitializer> logger) : IHostedService
{
    private readonly QdrantOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 10;
        const int delaySeconds = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
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
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                LogCollectionInitRetry(logger, ex, _options.CollectionName, attempt, maxAttempts, delaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
            catch (Exception ex)
            {
                LogCollectionInitFailed(logger, ex, _options.CollectionName);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CreatePayloadIndexesAsync(CancellationToken ct)
    {
        string[] keywordFields =
        [
            QdrantPayloadFields.SourceBook,
            QdrantPayloadFields.Version,
            QdrantPayloadFields.Category,
            QdrantPayloadFields.EntityName,
        ];
        foreach (var field in keywordFields)
            await client.CreatePayloadIndexAsync(_options.CollectionName, field, PayloadSchemaType.Keyword, cancellationToken: ct);

        string[] intFields = [QdrantPayloadFields.PageNumber, QdrantPayloadFields.ChunkIndex, QdrantPayloadFields.PageEnd];
        foreach (var field in intFields)
            await client.CreatePayloadIndexAsync(_options.CollectionName, field, PayloadSchemaType.Integer, cancellationToken: ct);

        LogPayloadIndexesCreated(logger, _options.CollectionName);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Qdrant collection '{Collection}' already exists")]
    private static partial void LogCollectionExists(ILogger logger, string collection);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created Qdrant collection '{Collection}' (size={Size}, distance=Cosine)")]
    private static partial void LogCollectionCreated(ILogger logger, string collection, int size);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to initialise Qdrant collection '{Collection}' (attempt {Attempt}/{Max}), retrying in {Delay}s")]
    private static partial void LogCollectionInitRetry(ILogger logger, Exception ex, string collection, int attempt, int max, int delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to initialise Qdrant collection '{Collection}' after all retries")]
    private static partial void LogCollectionInitFailed(ILogger logger, Exception ex, string collection);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created payload indexes on collection '{Collection}'")]
    private static partial void LogPayloadIndexesCreated(ILogger logger, string collection);
}
