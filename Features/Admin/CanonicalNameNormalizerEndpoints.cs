namespace DndMcpAICsharpFun.Features.Admin;

public static class CanonicalNameNormalizerEndpoints
{
    public static WebApplication MapCanonicalNameNormalizerEndpoints(this WebApplication app)
    {
        app.MapPost("/admin/canonical/normalize", async (
            CanonicalNameNormalizerService svc,
            bool dryRun = false,
            CancellationToken ct = default) =>
        {
            var report = await svc.NormalizeAsync(dryRun, ct);
            return Results.Ok(report);
        });
        return app;
    }
}
