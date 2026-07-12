using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Retrieval.Entities;

namespace DndMcpAICsharpFun.Features.CharacterAdvice;

/// <summary>
/// Validates a class exists (edition-pinned) and assembles cited build-option menus (subclasses,
/// feats, and — for casters only — level-1 spells) for a concept. Mirrors the class-lookup pattern
/// in <see cref="LevelUpAdviceService"/>: exact name + edition match only, never a fuzzy first-hit
/// (grounding contract — the service never invents options, only surfaces cited retrieval results).
/// </summary>
public sealed class BuildRecommenderService(IEntityRetrievalService retrieval, EntityOptionProvider options)
{
    private const string BuildEdition = "Edition2014";  // matches LevelUpAdviceService.LevelUpEdition — PHB-2014 pin
    private const int TopK = 20;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<BuildRecommendation> RecommendBuildOptionsAsync(
        string className, string concept, int? targetLevel, CancellationToken ct)
    {
        var classResults = await retrieval.SearchDiagnosticAsync(
            new EntitySearchQuery(
                QueryText: className,
                Type: EntityType.Class,
                SourceBook: null,
                Edition: BuildEdition,
                BookType: null,
                SettingTag: null,
                Keyword: null,
                CrNumericLte: null,
                CrNumericGte: null,
                SpellLevel: null,
                DamageType: null,
                TopK: TopK),
            ct);
        var classEntity = classResults.FirstOrDefault(
            r => string.Equals(r.Name, className, StringComparison.OrdinalIgnoreCase)
              && string.Equals(r.Edition, BuildEdition, StringComparison.OrdinalIgnoreCase));

        if (classEntity is null)
        {
            var available = classResults
                .Where(r => string.Equals(r.Edition, BuildEdition, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (available.Count == 0)   // the query text was the (absent) class name — do a broad class scan
            {
                var all = await retrieval.SearchDiagnosticAsync(
                    new EntitySearchQuery(
                        QueryText: "class",
                        Type: EntityType.Class,
                        SourceBook: null,
                        Edition: BuildEdition,
                        BookType: null,
                        SettingTag: null,
                        Keyword: null,
                        CrNumericLte: null,
                        CrNumericGte: null,
                        SpellLevel: null,
                        DamageType: null,
                        TopK: TopK),
                    ct);
                available = all.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            return BuildRecommendation.NotInCorpus(className, available);
        }

        var fields = classEntity.Fields.Deserialize<ClassFields>(JsonOpts);
        var hitDie = fields?.Hd is { } hd ? $"d{hd.Faces}" : null;
        var saves = fields?.Proficiency?.ToList() ?? [];
        var subclassTitle = fields?.SubclassTitle;
        var spellAbility = classEntity.Fields.ValueKind == JsonValueKind.Object
            && classEntity.Fields.TryGetProperty("spellcastingAbility", out var sa)
            && sa.ValueKind == JsonValueKind.String
            ? sa.GetString()
            : null;

        var subclasses = await options.SubclassOptions(className, BuildEdition, ct);
        var feats = await options.FeatOptions(BuildEdition, ct, concept);
        var spells = spellAbility is null
            ? (IReadOnlyList<CitedOption>)[]
            : await options.SpellOptions(className, spellLevel: 1, BuildEdition, ct, concept);

        return new BuildRecommendation(
            true, classEntity.Name, hitDie, spellAbility, saves, subclassTitle,
            subclasses, feats, spells, []);
    }
}
