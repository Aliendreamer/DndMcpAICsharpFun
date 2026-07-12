using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Retrieval.Entities;

namespace DndMcpAICsharpFun.Features.CharacterAdvice;

/// <summary>
/// Cited subclass/feat/spell option menus, queried live from the typed <c>dnd_entities</c> store —
/// every option returned is a real entity with provenance, never invented. Each menu is a top-K
/// sample (see <see cref="TopK"/>), not an exhaustive list of every matching entity.
/// </summary>
public sealed class EntityOptionProvider(IEntityRetrievalService retrieval)
{
    private const int TopK = 25;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Subclass options for a given class. Subclasses have no dedicated class-name query field in
    /// <see cref="EntitySearchQuery"/>, so this queries Type=Subclass by class-name text (via
    /// <see cref="IEntityRetrievalService.SearchDiagnosticAsync"/>, which carries the raw
    /// <c>Fields</c> JSON) and post-filters on <see cref="SubclassFields.ClassName"/> so only
    /// subclasses that truly belong to <paramref name="className"/> are returned.
    /// </summary>
    public async Task<IReadOnlyList<CitedOption>> SubclassOptions(
        string className, string edition, CancellationToken ct)
    {
        var query = new EntitySearchQuery(
            QueryText: className,
            Type: EntityType.Subclass,
            SourceBook: null,
            Edition: edition,
            BookType: null,
            SettingTag: null,
            Keyword: null,
            CrNumericLte: null,
            CrNumericGte: null,
            SpellLevel: null,
            DamageType: null,
            TopK: TopK);
        var results = await retrieval.SearchDiagnosticAsync(query, ct);

        var options = new List<CitedOption>();
        foreach (var r in results)
        {
            var fields = r.Fields.Deserialize<SubclassFields>(JsonOpts);
            if (fields is null || !string.Equals(fields.ClassName, className, StringComparison.OrdinalIgnoreCase))
                continue;
            options.Add(new CitedOption(r.Id, r.Name, r.SourceBook));
        }
        return options;
    }

    /// <summary>Feat options for an edition — a top-K sample of Type=Feat entities.</summary>
    public async Task<IReadOnlyList<CitedOption>> FeatOptions(string edition, CancellationToken ct, string? concept = null)
    {
        var query = new EntitySearchQuery(
            QueryText: string.IsNullOrWhiteSpace(concept) ? "feat" : concept,
            Type: EntityType.Feat,
            SourceBook: null,
            Edition: edition,
            BookType: null,
            SettingTag: null,
            Keyword: null,
            CrNumericLte: null,
            CrNumericGte: null,
            SpellLevel: null,
            DamageType: null,
            TopK: TopK);
        var results = await retrieval.SearchAsync(query, ct);
        return results.Select(r => new CitedOption(r.Id, r.Name, r.SourceBook)).ToList();
    }

    /// <summary>
    /// Spell options for a class/level/edition — a top-K sample of Type=Spell entities at
    /// <paramref name="spellLevel"/>. Slice 1: no class-list post-filter yet (spells aren't
    /// tagged by class in the typed store), so this returns the level's spells for the edition
    /// with the class name only as query text; a class-list post-filter is a later refinement.
    /// </summary>
    public async Task<IReadOnlyList<CitedOption>> SpellOptions(
        string className, int spellLevel, string edition, CancellationToken ct, string? concept = null)
    {
        var query = new EntitySearchQuery(
            QueryText: string.IsNullOrWhiteSpace(concept) ? className : concept,
            Type: EntityType.Spell,
            SourceBook: null,
            Edition: edition,
            BookType: null,
            SettingTag: null,
            Keyword: null,
            CrNumericLte: null,
            CrNumericGte: null,
            SpellLevel: spellLevel,
            DamageType: null,
            TopK: TopK);
        var results = await retrieval.SearchAsync(query, ct);
        return results.Select(r => new CitedOption(r.Id, r.Name, r.SourceBook)).ToList();
    }
}
