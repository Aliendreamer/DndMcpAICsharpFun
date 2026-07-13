using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;

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

        group.MapGet("/retrieval/entities/duplicates", async (
            EntityDuplicateService svc, CancellationToken ct) =>
                Results.Ok(await svc.FindDuplicatesAsync(ct)));

        group.MapPost("/retrieval/entities/compact", async (
            EntityDuplicateService svc, bool apply = false, CancellationToken ct = default) =>
                Results.Ok(await svc.CompactAsync(apply, ct)));

        group.MapPost("/retrieval/backfill-source-keys", async (
            RetrievalBackfillService svc, CancellationToken ct) =>
        {
            var perBook = await svc.BackfillAsync(ct);
            var total = perBook.Values.Sum();
            return Results.Ok(new { perBook, total });
        });

        return group;
    }
}