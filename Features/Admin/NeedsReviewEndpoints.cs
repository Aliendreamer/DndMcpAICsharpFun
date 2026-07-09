namespace DndMcpAICsharpFun.Features.Admin;

public static class NeedsReviewEndpoints
{
    public static RouteGroupBuilder MapNeedsReview(this RouteGroupBuilder group)
    {
        // GET /admin/entities/needs-review?book=&reason=&offset=&limit=
        group.MapGet("/entities/needs-review", async (
            string? book,
            string? reason,
            int? offset,
            int? limit,
            NeedsReviewService svc,
            CancellationToken ct) =>
        {
            var off = offset is > 0 ? offset.Value : 0;
            var lim = limit is > 0 ? limit.Value : 50;
            var result = await svc.ListAsync(book, reason, off, lim, ct);
            return Results.Ok(result);
        });

        // GET /admin/entities/{id}
        group.MapGet("/entities/{id}", async (
            string id,
            NeedsReviewService svc,
            CancellationToken ct) =>
        {
            var entity = await svc.GetAsync(id, ct);
            return entity is null ? Results.NotFound() : Results.Ok(entity);
        });

        // POST /admin/entities/{id}/resolve
        group.MapPost("/entities/{id}/resolve", async (
            string id,
            ResolveRequest body,
            NeedsReviewService svc,
            CancellationToken ct) =>
        {
            bool found;
            try
            {
                found = await svc.ResolveAsync(id, body.Action, body.Name, body.Fields, ct);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            return found ? Results.Ok() : Results.NotFound();
        });

        // POST /admin/entities/needs-review/accept
        group.MapPost("/entities/needs-review/accept", async (
            BulkAcceptRequest? body,
            NeedsReviewService svc,
            CancellationToken ct) =>
        {
            var cleared = await svc.BulkAcceptAsync(body?.Book, body?.Reason, ct);
            return Results.Ok(new BulkAcceptResponse(cleared));
        });

        return group;
    }
}