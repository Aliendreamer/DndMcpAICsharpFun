using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Features.Retrieval;

public static class QdrantPayloadMapper
{
    public static ChunkMetadata ToChunkMetadata(IReadOnlyDictionary<string, Value> payload)
    {
        return new ChunkMetadata(
            SourceBook: GetString(payload, QdrantPayloadFields.SourceBook),
            Version: ParseEnum<DndVersion>(payload, QdrantPayloadFields.Version),
            Category: ParseEnum<ContentCategory>(payload, QdrantPayloadFields.Category),
            EntityName: GetStringOrNull(payload, QdrantPayloadFields.EntityName),
            Chapter: GetString(payload, QdrantPayloadFields.Chapter),
            PageNumber: GetInt(payload, QdrantPayloadFields.PageNumber),
            ChunkIndex: GetInt(payload, QdrantPayloadFields.ChunkIndex),
            PageEnd: GetIntOrNull(payload, QdrantPayloadFields.PageEnd),
            SectionTitle: GetStringOrNull(payload, QdrantPayloadFields.SectionTitle),
            SectionStart: GetIntOrNull(payload, QdrantPayloadFields.SectionStart),
            SectionEnd: GetIntOrNull(payload, QdrantPayloadFields.SectionEnd));
    }

    private static int? GetIntOrNull(IReadOnlyDictionary<string, Value> payload, string key)
        => payload.TryGetValue(key, out var v) ? (int)v.IntegerValue : null;

    public static string GetText(IReadOnlyDictionary<string, Value> payload)
        => GetString(payload, QdrantPayloadFields.Text);

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
