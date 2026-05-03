using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Retrieval;

public static class RetrievalEndpoints
{
    public static WebApplication MapRetrievalEndpoints(this WebApplication app)
    {
        app.MapGet("/retrieval/search", SearchPublic);
        app.MapGroup("/admin").MapGet("/retrieval/search", SearchDiagnostic);
        return app;
    }

    private static async Task<IResult> SearchPublic(
        string? q,
        string? version,
        string? category,
        string? sourceBook,
        string? entityName,
        string? bookType,
        int topK = 5,
        IRagRetrievalService retrieval = default!,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Results.BadRequest("Query parameter 'q' is required.");

        var query = BuildQuery(q, version, category, sourceBook, entityName, bookType, topK);
        var results = await retrieval.SearchAsync(query, ct);
        return Results.Ok(results);
    }

    private static async Task<IResult> SearchDiagnostic(
        string? q,
        string? version,
        string? category,
        string? sourceBook,
        string? entityName,
        string? bookType,
        int topK = 5,
        IRagRetrievalService retrieval = default!,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Results.BadRequest("Query parameter 'q' is required.");

        var query = BuildQuery(q, version, category, sourceBook, entityName, bookType, topK);
        var results = await retrieval.SearchDiagnosticAsync(query, ct);
        return Results.Ok(results);
    }

    private static RetrievalQuery BuildQuery(
        string q,
        string? version,
        string? category,
        string? sourceBook,
        string? entityName,
        string? bookType,
        int topK)
    {
        DndVersion? parsedVersion = Enum.TryParse<DndVersion>(version, ignoreCase: true, out var v) ? v : null;
        ContentCategory? parsedCategory = Enum.TryParse<ContentCategory>(category, ignoreCase: true, out var c) ? c : null;
        BookType? parsedBookType = Enum.TryParse<BookType>(bookType, ignoreCase: true, out var b) ? b : null;

        return new RetrievalQuery(
            QueryText: q,
            Version: parsedVersion,
            Category: parsedCategory,
            SourceBook: sourceBook,
            EntityName: entityName,
            BookType: parsedBookType,
            TopK: topK);
    }
}
