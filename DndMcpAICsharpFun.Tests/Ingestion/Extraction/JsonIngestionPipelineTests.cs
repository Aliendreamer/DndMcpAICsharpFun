using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.VectorStore;
using NSubstitute;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public sealed class JsonIngestionPipelineTests
{
    private readonly IEntityJsonStore _store = Substitute.For<IEntityJsonStore>();
    private readonly IEmbeddingIngestor _ingestor = Substitute.For<IEmbeddingIngestor>();
    private readonly JsonIngestionPipeline _pipeline;

    public JsonIngestionPipelineTests()
    {
        _pipeline = new JsonIngestionPipeline(_store, _ingestor);
    }

    [Fact]
    public async Task IngestAsync_CallsMergePassThenEmbeds()
    {
        var entity = new ExtractedEntity(1, "PHB", "Edition2014", false, "Spell", "Fireball",
            new JsonObject { ["level"] = 3, ["description"] = "Big fire ball." });

        _store.LoadAllPagesAsync(42).Returns(
            Task.FromResult<IReadOnlyList<IReadOnlyList<ExtractedEntity>>>(
                [[entity]]));

        await _pipeline.IngestAsync(bookId: 42, fileHash: "abc123");

        await _store.Received(1).RunMergePassAsync(42, Arg.Any<CancellationToken>());
        await _ingestor.Received(1).IngestAsync(
            Arg.Is<IList<ContentChunk>>(chunks =>
                chunks.Count == 1 &&
                chunks[0].Text == "Big fire ball." &&
                chunks[0].Metadata.Category == ContentCategory.Spell &&
                chunks[0].Metadata.EntityName == "Fireball"),
            "abc123",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_SkipsEntitiesWithEmptyDescription()
    {
        var entity = new ExtractedEntity(1, "PHB", "Edition2014", false, "Rule", "Empty",
            new JsonObject { ["description"] = "   " });

        _store.LoadAllPagesAsync(42).Returns(
            Task.FromResult<IReadOnlyList<IReadOnlyList<ExtractedEntity>>>(
                [[entity]]));

        await _pipeline.IngestAsync(bookId: 42, fileHash: "abc123");

        await _ingestor.Received(1).IngestAsync(
            Arg.Is<IList<ContentChunk>>(chunks => chunks.Count == 0),
            "abc123",
            Arg.Any<CancellationToken>());
    }
}
