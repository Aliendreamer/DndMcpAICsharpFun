using System.ComponentModel;
using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
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
    private static readonly JsonSerializerOptions _json =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [McpServerTool, Description(
        "Search D&D rules, lore, and narrative text using semantic similarity. " +
        "Use for rules lookups, how-does-X-work questions, and prose descriptions.")]
    public async Task<string> search_lore(
        [Description("Natural language question or keyword")] string query,
        [Description("Edition filter: Edition2014 or Edition2024")] string? version = null,
        [Description("Content category: Spell, Monster, Class, Race, Background, Item, Rule, " +
                     "Combat, Adventuring, Condition, God, Plane, Treasure, Encounter, Trap, Trait, Lore")]
        string? category = null,
        [Description("Maximum number of results (default 5)")] int topK = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Error: query must not be empty.";

        var dndVersion = Enum.TryParse<DndVersion>(version, out var v) ? v : (DndVersion?)null;
        var contentCategory = Enum.TryParse<ContentCategory>(category, out var c) ? c : (ContentCategory?)null;

        var results = await ragService.SearchAsync(
            new RetrievalQuery(query, dndVersion, contentCategory, TopK: topK), ct);

        return results.Count == 0
            ? "No lore results found."
            : JsonSerializer.Serialize(results.Select(r => new
            {
                title = r.Metadata.SectionTitle ?? r.Metadata.Chapter,
                text = r.Text,
                sourceBook = r.Metadata.SourceBook,
                category = r.Metadata.Category.ToString(),
                score = r.Score
            }), _json);
    }

    [McpServerTool, Description(
        "Search structured D&D entities: spells, monsters, classes, subclasses, items, feats, races, and more. " +
        "Use for stat lookups, finding entities by type, or filtering by CR, spell level, or keywords.")]
    public async Task<string> search_entities(
        [Description("Search text — entity name or description")] string query,
        [Description("Entity type: Spell, Monster, Class, Subclass, Race, Subrace, Background, Feat, " +
                     "Weapon, Armor, Item, MagicItem, Trap, God, Plane, Faction, Location, Condition, Lore, Rule, " +
                     "DiseasePoison, VehicleMount")]
        string? type = null,
        [Description("Edition: Edition2014 or Edition2024")] string? edition = null,
        [Description("Trait tag keyword e.g. Amphibious, Pack Tactics, Undead Fortitude")] string? keyword = null,
        [Description("Maximum challenge rating inclusive (for monsters)")] double? crMax = null,
        [Description("Spell level 0–9 (for spells)")] int? spellLevel = null,
        [Description("Restrict to SRD 5.1 entities only")] bool? srd = null,
        [Description("Restrict to SRD 5.2.1 entities only")] bool? srd52 = null,
        [Description("Number of results (default 10)")] int topK = 10,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Error: query must not be empty.";

        var entityType = Enum.TryParse<EntityType>(type, out var t) ? t : (EntityType?)null;

        var results = await entityService.SearchAsync(
            new EntitySearchQuery(
                QueryText: query,
                Type: entityType,
                SourceBook: null,
                Edition: edition,
                BookType: null,
                SettingTag: null,
                Keyword: keyword,
                CrNumericLte: crMax,
                CrNumericGte: null,
                SpellLevel: spellLevel,
                DamageType: null,
                TopK: topK,
                Srd: srd,
                Srd52: srd52), ct);

        if (results.Count == 0)
            return "No entities found.";

        return JsonSerializer.Serialize(results.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            type = r.Type.ToString(),
            sourceBook = r.SourceBook,
            edition = r.Edition,
            snippet = r.Snippet,
            score = r.Score
        }), _json);
    }

    [McpServerTool, Description(
        "Fetch a single D&D entity by its canonical ID (e.g. 'phb.spell.fireball', " +
        "'tce.subclass.circle-of-spores'). Use after search_entities to get full details.")]
    public async Task<string> get_entity(
        [Description("Canonical entity ID from a previous search_entities result")] string id,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "Error: id must not be empty.";

        var result = await entityService.GetByIdAsync(id, ct);
        if (result is null)
            return $"Entity not found: {id}";

        var e = result.Envelope;
        return JsonSerializer.Serialize(new
        {
            id = e.Id,
            name = e.Name,
            type = e.Type.ToString(),
            sourceBook = e.SourceBook,
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
                text = r.Text,
                score = r.Score
            }), _json);
    }
}
