using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

public static class EntityRetrievalEndpoints
{
    // Upper bound on the anonymous public result count (SEC-10). Admin diagnostic is unbounded.
    private const int MaxPublicTopK = 50;

    public static WebApplication MapEntityRetrievalEndpoints(this WebApplication app)
    {
        app.MapGet("/retrieval/entities/{id}", GetById).RequireRateLimiting("retrieval");
        app.MapGet("/retrieval/entities/search", SearchPublic).RequireRateLimiting("retrieval");
        app.MapGet("/retrieval/entities/list", ListPublic).RequireRateLimiting("retrieval");
        app.MapGroup("/admin").MapGet("/retrieval/entities/search", SearchDiagnostic);
        return app;
    }

    private static async Task<IResult> GetById(string id, IEntityRetrievalService svc, CancellationToken ct)
    {
        var result = await svc.GetByIdAsync(id, ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static Task<IResult> SearchPublic(
        string? q, string? type, string? sourceBook, string? edition, string? bookType,
        string? settingTag, string? keyword, double? crNumeric_lte, double? crNumeric_gte,
        int? spellLevel, string? damageType, bool? srd, bool? srd52, bool? basicRules2024,
        IEntityRetrievalService svc, CancellationToken ct, int topK = 10)
        => SearchCore(q, type, sourceBook, edition, bookType, settingTag, keyword,
            crNumeric_lte, crNumeric_gte, spellLevel, damageType, srd, srd52, basicRules2024,
            svc, ct, topK, diagnostic: false);

    private static Task<IResult> SearchDiagnostic(
        string? q, string? type, string? sourceBook, string? edition, string? bookType,
        string? settingTag, string? keyword, double? crNumeric_lte, double? crNumeric_gte,
        int? spellLevel, string? damageType, bool? srd, bool? srd52, bool? basicRules2024,
        IEntityRetrievalService svc, CancellationToken ct, int topK = 10)
        => SearchCore(q, type, sourceBook, edition, bookType, settingTag, keyword,
            crNumeric_lte, crNumeric_gte, spellLevel, damageType, srd, srd52, basicRules2024,
            svc, ct, topK, diagnostic: true);

    private static async Task<IResult> SearchCore(
        string? q, string? type, string? sourceBook, string? edition, string? bookType,
        string? settingTag, string? keyword, double? crLte, double? crGte,
        int? spellLevel, string? damageType, bool? srd, bool? srd52, bool? basicRules2024,
        IEntityRetrievalService svc, CancellationToken ct, int topK, bool diagnostic)
    {
        if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest("Query parameter 'q' is required.");
        if (!diagnostic) topK = Math.Clamp(topK, 1, MaxPublicTopK);
        var query = BuildQuery(q, type, sourceBook, edition, bookType, settingTag, keyword,
            crLte, crGte, spellLevel, damageType, topK, srd, srd52, basicRules2024);
        var results = diagnostic
            ? (object)await svc.SearchDiagnosticAsync(query, ct)
            : await svc.SearchAsync(query, ct);
        return Results.Ok(results);
    }

    // entity-set-query: complete deterministic filter-set (not similarity top-K). No `q` — filters only.
    private static async Task<IResult> ListPublic(
        string? type, string? sourceBook, string? edition, string? bookType,
        string? settingTag, string? keyword, double? crNumeric_lte, double? crNumeric_gte,
        int? spellLevel, string? damageType, bool? srd, bool? srd52, bool? basicRules2024,
        string? castableByClass,
        IEntityRetrievalService svc, CancellationToken ct, int limit = 50)
    {
        var query = BuildQuery(string.Empty, type, sourceBook, edition, bookType, settingTag, keyword,
            crNumeric_lte, crNumeric_gte, spellLevel, damageType, limit, srd, srd52, basicRules2024)
            with { CastableByClass = castableByClass };
        var result = await svc.ListAsync(query, limit, ct);
        return Results.Ok(result);
    }

    private static EntitySearchQuery BuildQuery(
        string q, string? type, string? sourceBook, string? edition, string? bookType,
        string? settingTag, string? keyword, double? crLte, double? crGte,
        int? spellLevel, string? damageType, int topK,
        bool? srd, bool? srd52, bool? basicRules2024)
    {
        EntityType? parsedType = Enum.TryParse<EntityType>(type, ignoreCase: true, out var t) ? t : null;
        return new EntitySearchQuery(
            q, parsedType, sourceBook, edition, bookType, settingTag, keyword,
            crLte, crGte, spellLevel, damageType, topK <= 0 ? 10 : topK,
            Srd: srd, Srd52: srd52, BasicRules2024: basicRules2024);
    }
}