namespace DndMcpAICsharpFun.Features.Admin;

public static class CanonicalValidationEndpoints
{
    public static WebApplication MapCanonicalValidationEndpoints(this WebApplication app)
    {
        app.MapPost("/admin/canonical/validate", async (
            CanonicalValidationService svc, CancellationToken ct) =>
        {
            var report = await svc.ValidateAsync(ct);
            return report.Failures.Count > 0
                ? Results.UnprocessableEntity(report)
                : Results.Ok(report);
        });
        return app;
    }
}
