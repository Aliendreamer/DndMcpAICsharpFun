using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public sealed class QdrantPayloadMapperTests
{
    private static Dictionary<string, Value> FullPayload(string? entityName = "Fireball") =>
        new Dictionary<string, Value>
        {
            [QdrantPayloadFields.Text]       = "A bright streak flashes...",
            [QdrantPayloadFields.SourceBook] = "PHB",
            [QdrantPayloadFields.Version]    = "Edition2014",
            [QdrantPayloadFields.Category]   = "Spell",
            [QdrantPayloadFields.Chapter]    = "Chapter 11",
            [QdrantPayloadFields.PageNumber] = 241L,
            [QdrantPayloadFields.ChunkIndex] = 3L,
        }.Also(d => { if (entityName != null) d[QdrantPayloadFields.EntityName] = entityName; });

    [Fact]
    public void ToChunkMetadata_MapsAllFields()
    {
        ChunkMetadata result = QdrantPayloadMapper.ToChunkMetadata(FullPayload());

        Assert.Equal("PHB", result.SourceBook);
        Assert.Equal(DndVersion.Edition2014, result.Version);
        Assert.Equal(ContentCategory.Spell, result.Category);
        Assert.Equal("Fireball", result.EntityName);
        Assert.Equal("Chapter 11", result.Chapter);
        Assert.Equal(241, result.PageNumber);
        Assert.Equal(3, result.ChunkIndex);
    }

    [Fact]
    public void ToChunkMetadata_EntityNameNull_WhenKeyAbsent()
    {
        var payload = FullPayload(entityName: null);
        ChunkMetadata result = QdrantPayloadMapper.ToChunkMetadata(payload);
        Assert.Null(result.EntityName);
    }

    [Fact]
    public void ToChunkMetadata_UnknownVersion_MapsToDefault()
    {
        var payload = FullPayload();
        payload[QdrantPayloadFields.Version] = "NotAVersion";
        ChunkMetadata result = QdrantPayloadMapper.ToChunkMetadata(payload);
        Assert.Equal(default(DndVersion), result.Version);
    }

    [Fact]
    public void ToChunkMetadata_UnknownCategory_MapsToDefault()
    {
        var payload = FullPayload();
        payload[QdrantPayloadFields.Category] = "NotACategory";
        ChunkMetadata result = QdrantPayloadMapper.ToChunkMetadata(payload);
        Assert.Equal(default(ContentCategory), result.Category);
    }

    [Fact]
    public void GetText_ReturnsTextFieldValue()
    {
        string result = QdrantPayloadMapper.GetText(FullPayload());
        Assert.Equal("A bright streak flashes...", result);
    }
}

file static class DictionaryExtensions
{
    public static Dictionary<TKey, TValue> Also<TKey, TValue>(
        this Dictionary<TKey, TValue> dict, Action<Dictionary<TKey, TValue>> configure)
        where TKey : notnull
    {
        configure(dict);
        return dict;
    }
}
