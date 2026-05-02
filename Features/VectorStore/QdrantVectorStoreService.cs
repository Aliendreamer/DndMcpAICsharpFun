using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Features.VectorStore;

[ExcludeFromCodeCoverage]
public sealed partial class QdrantVectorStoreService(
    QdrantClient client,
    IOptions<QdrantOptions> options,
    ILogger<QdrantVectorStoreService> logger) : IVectorStoreService
{
    private readonly string _collectionName = options.Value.CollectionName;

    public async Task UpsertAsync(
        IList<(ContentChunk Chunk, float[] Vector, string FileHash)> points,
        CancellationToken ct = default)
    {
        var qdrantPoints = points.Select(p => BuildPoint(p.Chunk, p.Vector, p.FileHash)).ToList();
        LogUpsertStart(logger, qdrantPoints.Count, _collectionName);
        var sw = Stopwatch.StartNew();
        await client.UpsertAsync(_collectionName, qdrantPoints, cancellationToken: ct);
        LogUpsertDone(logger, qdrantPoints.Count, _collectionName, sw.ElapsedMilliseconds);
    }

    public async Task DeleteByHashAsync(string fileHash, int chunkCount, CancellationToken ct = default)
    {
        var ids = Enumerable.Range(0, chunkCount)
            .Select(i => DerivePointId(fileHash, i))
            .ToList();
        await client.DeleteAsync(_collectionName, ids, cancellationToken: ct);
    }

    private static PointStruct BuildPoint(ContentChunk chunk, float[] vector, string fileHash)
    {
        var meta = chunk.Metadata;
        var pointId = DerivePointId(fileHash, meta.ChunkIndex);

        var point = new PointStruct
        {
            Id = pointId,
            Vectors = vector,
        };

        point.Payload[QdrantPayloadFields.Text]       = chunk.Text;
        point.Payload[QdrantPayloadFields.SourceBook] = meta.SourceBook;
        point.Payload[QdrantPayloadFields.Version]    = meta.Version.ToString();
        point.Payload[QdrantPayloadFields.Category]   = meta.Category.ToString();
        point.Payload[QdrantPayloadFields.Chapter]    = meta.Chapter;
        point.Payload[QdrantPayloadFields.PageNumber] = (long)meta.PageNumber;
        point.Payload[QdrantPayloadFields.ChunkIndex] = (long)meta.ChunkIndex;

        if (meta.EntityName is not null)
            point.Payload[QdrantPayloadFields.EntityName] = meta.EntityName;

        if (meta.PageEnd.HasValue)
            point.Payload[QdrantPayloadFields.PageEnd] = (long)meta.PageEnd.Value;

        if (meta.SectionTitle is not null)
            point.Payload[QdrantPayloadFields.SectionTitle] = meta.SectionTitle;

        if (meta.SectionStart.HasValue)
            point.Payload[QdrantPayloadFields.SectionStart] = (long)meta.SectionStart.Value;

        if (meta.SectionEnd.HasValue)
            point.Payload[QdrantPayloadFields.SectionEnd] = (long)meta.SectionEnd.Value;

        return point;
    }

    private static Guid DerivePointId(string fileHash, int chunkIndex)
    {
        var input = Encoding.UTF8.GetBytes(fileHash + chunkIndex.ToString());
        var hash = SHA256.HashData(input);
        // Use first 16 bytes as a deterministic Guid
        return new Guid(hash[..16]);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Upserting {Count} vectors into {Collection}")]
    private static partial void LogUpsertStart(ILogger logger, int count, string collection);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Upserted {Count} vectors into {Collection} in {ElapsedMs}ms")]
    private static partial void LogUpsertDone(ILogger logger, int count, string collection, long elapsedMs);
}
