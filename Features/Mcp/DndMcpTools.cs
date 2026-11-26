using System.ComponentModel;
using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;

using ModelContextProtocol.Server;

namespace DndMcpAICsharpFun.Features.Mcp;

[McpServerToolType]
public sealed class DndMcpTools(
    IRagRetrievalService ragService,
    IEntityRetrievalService entityService,
    IFusedRetrievalService fusedService)
{
    // A model reads this, not a browser: "1d6 + 4" must not arrive as "1d6 + 4".
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [McpServerTool, Description(
        "Search D&D rules, lore, and narrative text using semantic similarity. " +
        "Use for rules lookups, how-does-X-work questions, and prose descriptions.")]
    public async Task<string> search_lore(
        [Description("Natural language question or keyword")] string query,
        [Description("Maximum number of results (default 5)")] int topK = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Error: query must not be empty.";

        var results = await ragService.SearchAsync(new RetrievalQuery(query, TopK: topK), ct);

        return results.Count == 0
            ? "No lore results found."
            : JsonSerializer.Serialize(results.Select(r => new
            {
                title = r.Metadata.SectionTitle ?? r.Metadata.Chapter,
                text = r.Text,
                sourceBook = r.Metadata.SourceBook,
                book = BookCatalog.Title(r.Metadata.SourceBook),
                page = BookCatalog.PrintedPage(r.Metadata.SourceBook, r.Metadata.PageNumber),
                category = r.Metadata.Category.ToString(),
                score = r.Score
            }), _json);
    }

    [McpServerTool, Description(
        "Search structured D&D entities: spells, monsters, classes, subclasses, items, feats, races, and more. " +
        "Use for stat lookups, finding entities by type, or filtering by CR, spell level, or keywords. " +
        "The first results carry their text; a text ending in … is cut — get_entity returns it whole. " +
        "A long text may list its numbered parts; get_entity with that part number returns the part.")]
    public async Task<string> search_entities(
        [Description("Search text — entity name or description")] string query,
        [Description("Entity type: Spell, Monster, Class, Subclass, Race, Subrace, Background, Feat, " +
                     "Weapon, Armor, Item (any item, magic items too), MagicItem, Trap, God, Plane, Faction, Location, " +
                     "Condition, Lore, Rule, DiseasePoison, VehicleMount")]
        string? type = null,
        [Description("Trait tag keyword e.g. Amphibious, Pack Tactics, Undead Fortitude")] string? keyword = null,
        [Description("Maximum challenge rating inclusive (for monsters)")] double? crMax = null,
        [Description("Spell level 0–9 (for spells)")] int? spellLevel = null,
        [Description("Number of results (default 6)")] int topK = 6,
        [Description("Only this book, when the question names one: a key (MM, MTF) or the book's name")]
        string? sourceBook = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Error: query must not be empty.";

        var entityType = Enum.TryParse<EntityType>(type, out var t) ? t : (EntityType?)null;
        var bookKey = BookCatalog.ResolveKey(sourceBook);

        var results = await SearchEntitiesAsync(bookKey);
        // A book the entity is not in (a wrong guess by the model) must not hide the answer.
        if (results.Count == 0 && bookKey is not null)
            results = await SearchEntitiesAsync(null);

        if (results.Count == 0)
            return "No entities found.";

        return SerializeEntityHits(results);

        Task<IList<EntitySearchResult>> SearchEntitiesAsync(string? book) => entityService.SearchAsync(
            new EntitySearchQuery(
                QueryText: query,
                Type: entityType,
                SourceBook: book,
                Edition: null,
                BookType: null,
                SettingTag: null,
                Keyword: keyword,
                CrNumericLte: crMax,
                CrNumericGte: null,
                SpellLevel: spellLevel,
                DamageType: null,
                TopK: topK), ct);
    }

    private static string SerializeEntityHits(IList<EntitySearchResult> results) =>
        JsonSerializer.Serialize(results.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            type = r.Type.ToString(),
            sourceBook = r.SourceBook,
            book = BookCatalog.Title(r.SourceBook),
            page = BookCatalog.PrintedPage(r.SourceBook, r.Page),
            edition = r.Edition,
            text = r.Snippet,
            parts = r.Parts,
            score = r.Score
        }), _json);

    [McpServerTool, Description(
        "List the COMPLETE set of structured D&D entities matching filters — deterministic, not " +
        "similarity-ranked. Use for \"all/every/how many\" questions (e.g. all CR-5 monsters, all " +
        "level-3 fire spells). Returns compact rows {id,name,type,...} and the TRUE total (rows are " +
        "capped, with a truncation notice when there are more). For a semantic \"find me a ...\" use " +
        "search_entities; drill into any row's full detail with get_entity.")]
    public async Task<string> list_entities(
        [Description("Entity type: Spell, Monster, Class, Subclass, Race, Background, Feat, Item, " +
                     "MagicItem, Condition, God, and more")] string? type = null,
        [Description("Minimum challenge rating inclusive (for monsters)")] double? crMin = null,
        [Description("Maximum challenge rating inclusive (for monsters)")] double? crMax = null,
        [Description("Spell level 0–9 (for spells)")] int? spellLevel = null,
        [Description("Damage type e.g. fire, cold, acid")] string? damageType = null,
        [Description("Trait tag keyword e.g. Amphibious, Flying, Pack Tactics")] string? keyword = null,
        [Description("Source book key e.g. PHB, MM, XGE")] string? sourceBook = null,
        [Description("Only spells this class can learn, e.g. Wizard (forces type=Spell; combine with spellLevel)")]
        string? castableByClass = null,
        [Description("Max rows to return (default 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        var entityType = Enum.TryParse<EntityType>(type, out var t) ? t : (EntityType?)null;
        var result = await entityService.ListAsync(
            new EntitySearchQuery(
                QueryText: string.Empty,
                Type: entityType,
                SourceBook: sourceBook,
                Edition: null,
                BookType: null,
                SettingTag: null,
                Keyword: keyword,
                CrNumericLte: crMax,
                CrNumericGte: crMin,
                SpellLevel: spellLevel,
                DamageType: damageType,
                TopK: limit,
                CastableByClass: castableByClass), limit, ct);

        return JsonSerializer.Serialize(new
        {
            total = result.Total,
            returned = result.Returned,
            truncated = result.Total > result.Returned,
            note = result.Total > result.Returned
                ? $"{result.Total} match; showing {result.Returned}. Add filters to narrow the set."
                : null,
            rows = result.Rows.Select(r => new
            {
                id = r.Id,
                name = r.Name,
                type = r.Type.ToString(),
                sourceBook = r.SourceBook,
                book = BookCatalog.Title(r.SourceBook),
                page = BookCatalog.PrintedPage(r.SourceBook, r.Page),
                cr = r.Cr,
                spellLevel = r.SpellLevel,
                damageType = r.DamageType,
            }),
        }, _json);
    }

    [McpServerTool, Description(
        "Fetch a single D&D entity by its canonical ID (e.g. 'phb.spell.fireball', " +
        "'tce.subclass.circle-of-spores'). Use after search_entities to get full details.")]
    public async Task<string> get_entity(
        [Description("Canonical entity ID from a previous search_entities result")] string id,
        [Description("Only this numbered part of a long text, from the result's parts list")] int? part = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "Error: id must not be empty.";

        var result = await entityService.GetByIdAsync(id, ct);
        if (result is null)
            return $"Entity not found: {id}";

        var e = result.Envelope;
        if (part is { } n)
        {
            var text = EntityTextPieces.Part(e.CanonicalText, n);
            if (text is not null)
                return JsonSerializer.Serialize(new
                {
                    id = e.Id,
                    name = e.Name,
                    sourceBook = e.SourceBook,
                    book = BookCatalog.Title(e.SourceBook),
                    page = BookCatalog.PrintedPage(e.SourceBook, e.Page),
                    part = n,
                    canonicalText = text,
                }, _json);
        }
        return JsonSerializer.Serialize(new
        {
            id = e.Id,
            name = e.Name,
            type = e.Type.ToString(),
            sourceBook = e.SourceBook,
            book = BookCatalog.Title(e.SourceBook),
            page = BookCatalog.PrintedPage(e.SourceBook, e.Page),
            edition = e.Edition,
            canonicalText = e.CanonicalText,
            keywords = e.Keywords,
            srd = e.Srd,
            srd52 = e.Srd52,
            fields = e.Fields.ValueKind == JsonValueKind.Undefined ? (object?)null : e.Fields
        }, _json);
    }

    [McpServerTool, Description(
        "Fused cross-channel D&D search: embeds the query once, fetches candidate pools from both " +
        "prose passages (dnd_blocks) and structured entities (dnd_entities), reranks the combined " +
        "set with a cross-encoder, and returns the single best-ranked list across both sources. " +
        "Each result includes a 'source' field ('prose' or 'entity') so the caller knows its origin. " +
        "Prefer this tool when you need the most relevant context regardless of whether it is " +
        "rules-text or a structured entity.")]
    public async Task<string> search_dnd(
        [Description("Natural language question or keyword")] string query,
        [Description("Maximum number of results (default 5)")] int topK = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Error: query must not be empty.";

        var results = await fusedService.SearchAsync(query, topK, ct);

        return results.Count == 0
            ? "No results found."
            : JsonSerializer.Serialize(results.Select(r => new
            {
                source = r.Source,
                id = r.Id,
                title = r.Title,
                sourceBook = r.SourceBook,
                book = BookCatalog.Title(r.SourceBook),
                page = r.SourceBook is null ? null : BookCatalog.PrintedPage(r.SourceBook, r.Page),
                text = r.Text,
                score = r.Score
            }), _json);
    }
}