using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

namespace DndMcpAICsharpFun.Features.Admin;

public static class FivetoolsAdminEndpoints
{
    public static RouteGroupBuilder MapFivetoolsAdmin(this RouteGroupBuilder group)
    {
        group.MapPost("/5etools/import", ImportAll);
        group.MapGet("/5etools/sources", GetSources);
        return group;
    }

    private static async Task<IResult> ImportAll(
        FivetoolsIngestionService service,
        CancellationToken ct)
    {
        await service.ImportAllAsync(ct);
        return Results.Ok();
    }

    private static IResult GetSources(BookSourceRegistry registry, string? group)
    {
        var books = string.IsNullOrEmpty(group)
            ? registry.GetAll()
            : registry.GetAll()
                .Where(b => string.Equals(b.Group, group, StringComparison.OrdinalIgnoreCase))
                .ToList();
        return Results.Ok(books);
    }
}