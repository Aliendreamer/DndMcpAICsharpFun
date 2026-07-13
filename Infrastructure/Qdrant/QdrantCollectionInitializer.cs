using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Options;

using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Infrastructure.Qdrant;

[ExcludeFromCodeCoverage]
public sealed partial class QdrantCollectionInitializer(
    QdrantClient client,
    IOptions<QdrantOptions> options,
    QdrantSparseState sparseState,
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
                await EnsureCollectionAsync(_options.BlocksCollectionName, cancellationToken);
                await EnsureCollectionAsync(_options.EntitiesCollectionName, cancellationToken);
                await DetectSparseVectorSupportAsync(_options.BlocksCollectionName, cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                LogCollectionInitRetry(logger, ex, _options.BlocksCollectionName, attempt, maxAttempts, delaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
            catch (Exception ex)
            {
                LogCollectionInitFailed(logger, ex, _options.BlocksCollectionName);
                throw; // crash the host — Qdrant is required
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureCollectionAsync(string name, CancellationToken ct)
    {
        var quantization = QdrantQuantization.ConfigFor(_options.Quantization);
        if (await client.CollectionExistsAsync(name, ct))
        {
            LogCollectionExists(logger, name);
            await EnsureQuantizationAsync(name, ct); // in-place add for a pre-quantization collection
        }
        else
        {
            var isBlocks = string.Equals(name, _options.BlocksCollectionName, StringComparison.Ordinal);
            if (isBlocks && _options.HybridAlpha > 0)
            {
                await client.CreateCollectionAsync(
                    name,
                    new VectorParams { Size = (ulong)_options.VectorSize, Distance = Distance.Cosine, QuantizationConfig = quantization },
                    sparseVectorsConfig: ("text-sparse", new SparseVectorParams()),
                    cancellationToken: ct);
            }
            else
            {
                await client.CreateCollectionAsync(
                    name,
                    new VectorParams { Size = (ulong)_options.VectorSize, Distance = Distance.Cosine, QuantizationConfig = quantization },
                    cancellationToken: ct);
            }
            LogCollectionCreated(logger, name, _options.VectorSize);
        }

        if (string.Equals(name, _options.EntitiesCollectionName, StringComparison.Ordinal))
            await CreateEntityPayloadIndexesAsync(name, ct);
        else
            await CreatePayloadIndexesAsync(name, ct);
    }

    /// <summary>
    /// Adds scalar int8 quantization to an EXISTING collection in place (background re-quantization,
    /// no re-ingest). Idempotent: no-op when quantization is disabled or already configured.
    /// </summary>
    private async Task EnsureQuantizationAsync(string name, CancellationToken ct)
    {
        if (!_options.Quantization.Enabled) return;

        var info = await client.GetCollectionInfoAsync(name, ct);
        if (info.Config?.QuantizationConfig is not null) return; // already quantized

        var diff = QdrantQuantization.DiffFor(_options.Quantization);
        if (diff is null) return;

        await client.UpdateCollectionAsync(name, quantizationConfig: diff, cancellationToken: ct);
        LogQuantizationEnabled(logger, name);
    }

    private async Task DetectSparseVectorSupportAsync(string collectionName, CancellationToken ct)
    {
        if (_options.HybridAlpha <= 0)
        {
            sparseState.SparseSupported = false;
            return;
        }

        var info = await client.GetCollectionInfoAsync(collectionName, ct);
        var hasSparse = info.Config?.Params?.SparseVectorsConfig?.Map?.ContainsKey("text-sparse") == true;

        sparseState.SparseSupported = hasSparse;
        if (!hasSparse)
            LogSparseVectorsMissing(logger, collectionName);
    }

    private async Task CreatePayloadIndexesAsync(string collection, CancellationToken ct)
    {
        string[] keywordFields =
        [
            QdrantPayloadFields.SourceBook,
            QdrantPayloadFields.Version,
            QdrantPayloadFields.Category,
            QdrantPayloadFields.EntityName,
            QdrantPayloadFields.SectionTitle,
            QdrantPayloadFields.BookType,
            QdrantPayloadFields.SourceKey,
        ];
        foreach (var field in keywordFields)
            await client.CreatePayloadIndexAsync(collection, field, PayloadSchemaType.Keyword, cancellationToken: ct);

        string[] intFields =
        [
            QdrantPayloadFields.PageNumber,
            QdrantPayloadFields.ChunkIndex,
            QdrantPayloadFields.PageEnd,
            QdrantPayloadFields.SectionStart,
            QdrantPayloadFields.SectionEnd,
            QdrantPayloadFields.BlockOrder,
        ];
        foreach (var field in intFields)
            await client.CreatePayloadIndexAsync(collection, field, PayloadSchemaType.Integer, cancellationToken: ct);

        LogPayloadIndexesCreated(logger, collection);
    }

    private async Task CreateEntityPayloadIndexesAsync(string collection, CancellationToken ct)
    {
        string[] keywordFields =
        [
            EntityPayloadFields.Type,
            EntityPayloadFields.SourceBook,
            EntityPayloadFields.Edition,
            EntityPayloadFields.BookType,
            EntityPayloadFields.SettingTags,
            EntityPayloadFields.Keywords,
            EntityPayloadFields.DamageType,
            EntityPayloadFields.FirstBook,
            EntityPayloadFields.FirstEdition,
            EntityPayloadFields.FileHash,
            EntityPayloadFields.Srd,            // new
            EntityPayloadFields.Srd52,          // new
            EntityPayloadFields.BasicRules2024, // new
        ];
        foreach (var field in keywordFields)
            await client.CreatePayloadIndexAsync(collection, field, PayloadSchemaType.Keyword, cancellationToken: ct);

        await client.CreatePayloadIndexAsync(collection, EntityPayloadFields.SpellLevel, PayloadSchemaType.Integer, cancellationToken: ct);
        await client.CreatePayloadIndexAsync(collection, EntityPayloadFields.Page, PayloadSchemaType.Integer, cancellationToken: ct);
        await client.CreatePayloadIndexAsync(collection, EntityPayloadFields.CrNumeric, PayloadSchemaType.Float, cancellationToken: ct);

        LogPayloadIndexesCreated(logger, collection);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Enabled scalar int8 quantization on existing collection '{Collection}' (background re-quantization)")]
    private static partial void LogQuantizationEnabled(ILogger logger, string collection);

    [LoggerMessage(Level = LogLevel.Warning, Message = "'{Collection}' collection has no sparse vector support; hybrid search disabled until re-ingestion")]
    private static partial void LogSparseVectorsMissing(ILogger logger, string collection);
}