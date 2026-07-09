using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Retrieval;

public static class RetrievalEndpoints
{
    // Upper bound on the anonymous public result count so a caller cannot request an unbounded
    // number of embeddings + vector-search rows (SEC-10). The admin diagnostic path is unbounded.
    private const int MaxPublicTopK = 50;

    public static WebApplication MapRetrievalEndpoints(this WebApplication app)
    {
        app.MapGet("/retrieval/search", SearchPublic).RequireRateLimiting("retrieval");
        app.MapGroup("/admin").MapGet("/retrieval/search", SearchDiagnostic);
        return app;
    }

    // Query parameters bound as one unit so the public and diagnostic handlers share a single
    // parameter declaration (SIM-19). TopK is nullable so an absent value resolves to the default
    // of 5 rather than 0.
    private readonly record struct SearchRequest(
        string? Q,
        string? Version,
        string? Category,
        string? SourceBook,
        string? EntityName,
        string? BookType,
        int? TopK);

    private static Task<IResult> SearchPublic(
        [AsParameters] SearchRequest request, IRagRetrievalService retrieval, CancellationToken ct)
        => SearchCore(request, retrieval, diagnostic: false, ct);

    private static Task<IResult> SearchDiagnostic(
        [AsParameters] SearchRequest request, IRagRetrievalService retrieval, CancellationToken ct)
        => SearchCore(request, retrieval, diagnostic: true, ct);

    private static async Task<IResult> SearchCore(
        SearchRequest request, IRagRetrievalService retrieval, bool diagnostic, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Q))
            return Results.BadRequest("Query parameter 'q' is required.");

        var topK = request.TopK ?? 5;
        if (!diagnostic)
            topK = Math.Clamp(topK, 1, MaxPublicTopK);

        var query = BuildQuery(request, topK);
        var results = diagnostic
            ? await retrieval.SearchDiagnosticAsync(query, ct)
            : (object)await retrieval.SearchAsync(query, ct);
        return Results.Ok(results);
    }

    private static RetrievalQuery BuildQuery(SearchRequest request, int topK)
    {
        DndVersion? parsedVersion = Enum.TryParse<DndVersion>(request.Version, ignoreCase: true, out var v) ? v : null;
        ContentCategory? parsedCategory = Enum.TryParse<ContentCategory>(request.Category, ignoreCase: true, out var c) ? c : null;
        BookType? parsedBookType = Enum.TryParse<BookType>(request.BookType, ignoreCase: true, out var b) ? b : null;

        return new RetrievalQuery(
            QueryText: request.Q!,
            Version: parsedVersion,
            Category: parsedCategory,
            SourceBook: request.SourceBook,
            EntityName: request.EntityName,
            BookType: parsedBookType,
            TopK: topK);
    }
}