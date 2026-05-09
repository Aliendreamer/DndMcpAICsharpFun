using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

public static class EntityRetrievalEndpoints
{
    public static WebApplication MapEntityRetrievalEndpoints(this WebApplication app)
    {
        app.MapGet("/retrieval/entities/{id}", GetById);
        app.MapGet("/retrieval/entities/search", SearchPublic);
        app.MapGroup("/admin").MapGet("/retrieval/entities/search", SearchDiagnostic);
        return app;
    }

    private static async Task<IResult> GetById(string id, IEntityRetrievalService svc, CancellationToken ct)
    {
        var result = await svc.GetByIdAsync(id, ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> SearchPublic(
        string? q, string? type, string? sourceBook, string? edition, string? bookType,
        string? settingTag, string? keyword, double? crNumeric_lte, double? crNumeric_gte,
        int? spellLevel, string? damageType, int topK,
        bool? srd, bool? srd52, bool? basicRules2024,
        IEntityRetrievalService svc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest("Query parameter 'q' is required.");
        var results = await svc.SearchAsync(BuildQuery(q, type, sourceBook, edition, bookType, settingTag, keyword, crNumeric_lte, crNumeric_gte, spellLevel, damageType, topK, srd, srd52, basicRules2024), ct);
        return Results.Ok(results);
    }

    private static async Task<IResult> SearchDiagnostic(
        string? q, string? type, string? sourceBook, string? edition, string? bookType,
        string? settingTag, string? keyword, double? crNumeric_lte, double? crNumeric_gte,
        int? spellLevel, string? damageType, int topK,
        bool? srd, bool? srd52, bool? basicRules2024,
        IEntityRetrievalService svc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest("Query parameter 'q' is required.");
        var results = await svc.SearchDiagnosticAsync(BuildQuery(q, type, sourceBook, edition, bookType, settingTag, keyword, crNumeric_lte, crNumeric_gte, spellLevel, damageType, topK, srd, srd52, basicRules2024), ct);
        return Results.Ok(results);
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
