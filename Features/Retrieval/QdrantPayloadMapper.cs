using DndMcpAICsharpFun.Domain;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Features.Retrieval;

public static class QdrantPayloadMapper
{
    public static ChunkMetadata ToChunkMetadata(IReadOnlyDictionary<string, Value> payload)
    {
        return new ChunkMetadata(
            SourceBook: GetString(payload, "source_book"),
            Version: ParseEnum<DndVersion>(payload, "version"),
            Category: ParseEnum<ContentCategory>(payload, "category"),
            EntityName: GetStringOrNull(payload, "entity_name"),
            Chapter: GetString(payload, "chapter"),
            PageNumber: GetInt(payload, "page_number"),
            ChunkIndex: GetInt(payload, "chunk_index"));
    }

    public static string GetText(IReadOnlyDictionary<string, Value> payload)
        => GetString(payload, "text");

    private static string GetString(IReadOnlyDictionary<string, Value> payload, string key)
        => payload.TryGetValue(key, out var v) ? v.StringValue : string.Empty;

    private static string? GetStringOrNull(IReadOnlyDictionary<string, Value> payload, string key)
        => payload.TryGetValue(key, out var v) && v.HasStringValue ? v.StringValue : null;

    private static int GetInt(IReadOnlyDictionary<string, Value> payload, string key)
        => payload.TryGetValue(key, out var v) ? (int)v.IntegerValue : 0;

    private static T ParseEnum<T>(IReadOnlyDictionary<string, Value> payload, string key) where T : struct, Enum
    {
        if (payload.TryGetValue(key, out var v) && v.HasStringValue &&
            Enum.TryParse<T>(v.StringValue, out var result))
            return result;
        return default;
    }
}
