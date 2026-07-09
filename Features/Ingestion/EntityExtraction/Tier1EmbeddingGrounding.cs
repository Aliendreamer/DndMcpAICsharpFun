using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using Microsoft.Extensions.Options;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Tier 1 grounding: embeds the entity text and searches <c>dnd_blocks</c> scoped to the
/// entity's own source book (and, when a page is known, a +/- page window), comparing the
/// top hit's similarity against a configured floor.
/// </summary>
public sealed class Tier1EmbeddingGrounding(
    IEmbeddingService embeddings,
    IQdrantSearchClient qdrant,
    IOptions<QdrantOptions> qdrantOptions,
    IOptions<GroundingOptions> grounding) : ITier1Grounding
{
    public async Task<Tier1Result> GroundAsync(
        string entityText, string sourceBook, int? page, CancellationToken ct)
    {
        var groundingOptions = grounding.Value;

        var embedded = await embeddings.EmbedAsync([entityText], ct);
        var vector = embedded[0];

        var filter = BuildFilter(sourceBook, page, groundingOptions.PageWindow);

        var hits = await qdrant.SearchAsync(
            qdrantOptions.Value.BlocksCollectionName,
            vector,
            filter: filter,
            limit: 1,
            cancellationToken: ct);

        var score = hits.Count > 0 ? (double)hits[0].Score : 0.0;

        return new Tier1Result(BelowFloor: score < groundingOptions.SimilarityFloor, Score: score);
    }

    private static Filter BuildFilter(string sourceBook, int? page, int pageWindow)
    {
        var filter = new Filter();
        filter.Must.Add(KeywordCondition(QdrantPayloadFields.SourceBook, sourceBook));

        if (page is { } p)
        {
            var range = new Qdrant.Client.Grpc.Range { Gte = p - pageWindow, Lte = p + pageWindow };
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition { Key = QdrantPayloadFields.PageNumber, Range = range }
            });
        }

        return filter;
    }

    private static Condition KeywordCondition(string key, string value) =>
        new()
        {
            Field = new FieldCondition
            {
                Key = key,
                Match = new Match { Keyword = value }
            }
        };
}
