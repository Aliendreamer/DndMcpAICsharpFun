using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
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
    private readonly string _blocksCollectionName = options.Value.BlocksCollectionName;

    public async Task UpsertBlocksAsync(
        IList<(BlockChunk Chunk, float[] Vector, string FileHash)> points,
        CancellationToken ct = default)
    {
        var qdrantPoints = points.Select(p => BuildBlockPoint(p.Chunk, p.Vector, p.FileHash)).ToList();
        LogUpsertStart(logger, qdrantPoints.Count, _blocksCollectionName);
        var sw = Stopwatch.StartNew();
        await client.UpsertAsync(_blocksCollectionName, qdrantPoints, cancellationToken: ct);
        LogUpsertDone(logger, qdrantPoints.Count, _blocksCollectionName, sw.ElapsedMilliseconds);
    }

    public async Task DeleteBlocksByHashAsync(string fileHash, int blockCount, CancellationToken ct = default)
    {
        var ids = Enumerable.Range(0, blockCount)
            .Select(i => DerivePointId(fileHash, i))
            .ToList();
        await client.DeleteAsync(_blocksCollectionName, ids, cancellationToken: ct);
    }

    private static PointStruct BuildBlockPoint(BlockChunk chunk, float[] vector, string fileHash)
    {
        var meta = chunk.Metadata;
        var point = new PointStruct
        {
            Id = DerivePointId(fileHash, meta.GlobalIndex),
            Vectors = vector,
        };
        point.Payload[QdrantPayloadFields.Text]         = chunk.Text;
        point.Payload[QdrantPayloadFields.SourceBook]   = meta.SourceBook;
        point.Payload[QdrantPayloadFields.Version]      = meta.Version.ToString();
        point.Payload[QdrantPayloadFields.Category]     = meta.Category.ToString();
        point.Payload[QdrantPayloadFields.SectionTitle] = meta.SectionTitle;
        point.Payload[QdrantPayloadFields.SectionStart] = (long)meta.SectionStart;
        point.Payload[QdrantPayloadFields.SectionEnd]   = (long)meta.SectionEnd;
        point.Payload[QdrantPayloadFields.PageNumber]   = (long)meta.PageNumber;
        point.Payload[QdrantPayloadFields.BlockOrder]   = (long)meta.BlockOrder;
        point.Payload[QdrantPayloadFields.ChunkIndex]   = (long)meta.GlobalIndex;
        return point;
    }

    private static Guid DerivePointId(string fileHash, int chunkIndex)
    {
        var input = Encoding.UTF8.GetBytes(fileHash + chunkIndex.ToString());
        var hash = SHA256.HashData(input);
        return new Guid(hash[..16]);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Upserting {Count} vectors into {Collection}")]
    private static partial void LogUpsertStart(ILogger logger, int count, string collection);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Upserted {Count} vectors into {Collection} in {ElapsedMs}ms")]
    private static partial void LogUpsertDone(ILogger logger, int count, string collection, long elapsedMs);
}
