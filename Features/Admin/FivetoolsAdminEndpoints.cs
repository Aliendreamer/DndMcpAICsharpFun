using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

namespace DndMcpAICsharpFun.Features.Admin;

public static class FivetoolsAdminEndpoints
{
    public static RouteGroupBuilder MapFivetoolsAdmin(this RouteGroupBuilder group)
    {
        group.MapPost("/5etools/import", ImportAll);
        return group;
    }

    private static async Task<IResult> ImportAll(
        FivetoolsIngestionService service,
        CancellationToken ct)
    {
        await service.ImportAllAsync(ct);
        return Results.Ok();
    }
}
