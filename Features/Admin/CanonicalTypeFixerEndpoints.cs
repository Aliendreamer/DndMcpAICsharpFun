using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

namespace DndMcpAICsharpFun.Features.Admin;

public static class CanonicalTypeFixerEndpoints
{
    public static WebApplication MapCanonicalTypeFixerEndpoints(this WebApplication app)
    {
        app.MapPost("/admin/canonical/fix-types", FixTypes);
        return app;
    }

    private static async Task<IResult> FixTypes(
        string book,
        FivetoolsIngestionService fivetools,
        CanonicalTypeFixerService svc,
        CancellationToken ct)
    {
        var lookup = await fivetools.BuildTypeLookupAsync(ct);
        try
        {
            var result = await svc.FixTypesAsync(book, lookup, ct);
            return Results.Ok(new { result.Fixed, result.Unmatched, result.CrossRefsUpdated });
        }
        catch (FileNotFoundException)
        {
            return Results.NotFound($"No canonical JSON for book '{book}'");
        }
    }
}
