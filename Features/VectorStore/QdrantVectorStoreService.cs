using System.Security.Cryptography;
using System.Text;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Features.VectorStore;

public sealed class QdrantVectorStoreService : IVectorStoreService
{
    private readonly QdrantClient _client;
    private readonly string _collectionName;

    public QdrantVectorStoreService(QdrantClient client, IOptions<QdrantOptions> options)
    {
        _client = client;
        _collectionName = options.Value.CollectionName;
    }

    public async Task UpsertAsync(
        IList<(ContentChunk Chunk, float[] Vector, string FileHash)> points,
        CancellationToken ct = default)
    {
        var qdrantPoints = points.Select(p => BuildPoint(p.Chunk, p.Vector, p.FileHash)).ToList();
        await _client.UpsertAsync(_collectionName, qdrantPoints, cancellationToken: ct);
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

        point.Payload["text"] = chunk.Text;
        point.Payload["source_book"] = meta.SourceBook;
        point.Payload["version"] = meta.Version.ToString();
        point.Payload["category"] = meta.Category.ToString();
        point.Payload["chapter"] = meta.Chapter;
        point.Payload["page_number"] = (long)meta.PageNumber;
        point.Payload["chunk_index"] = (long)meta.ChunkIndex;

        if (meta.EntityName is not null)
            point.Payload["entity_name"] = meta.EntityName;

        return point;
    }

    private static Guid DerivePointId(string fileHash, int chunkIndex)
    {
        var input = Encoding.UTF8.GetBytes(fileHash + chunkIndex.ToString());
        var hash = SHA256.HashData(input);
        // Use first 16 bytes as a deterministic Guid
        return new Guid(hash[..16]);
    }
}
