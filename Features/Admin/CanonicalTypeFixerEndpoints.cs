using System.Text.RegularExpressions;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

namespace DndMcpAICsharpFun.Features.Admin;

public static partial class CanonicalTypeFixerEndpoints
{
    [GeneratedRegex(@"^[a-zA-Z0-9_\-]+$")]
    private static partial Regex BookSlugRegex();

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
        if (string.IsNullOrWhiteSpace(book) ||
            !BookSlugRegex().IsMatch(book))
            return Results.BadRequest("Invalid book slug.");

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
