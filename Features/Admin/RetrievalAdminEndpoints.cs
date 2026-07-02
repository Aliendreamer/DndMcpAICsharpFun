using DndMcpAICsharpFun.Features.Retrieval;

namespace DndMcpAICsharpFun.Features.Admin;

public static class RetrievalAdminEndpoints
{
    public static RouteGroupBuilder MapRetrievalAdmin(this RouteGroupBuilder group)
    {
        group.MapPost("/retrieval/bm25/rebuild-stats", async (
            IBm25CorpusStats bm25Stats, CancellationToken ct) =>
        {
            await bm25Stats.RebuildAsync(ct);
            return Results.Ok();
        });
        return group;
    }
}
