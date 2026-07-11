using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Resolution;
using DndMcpAICsharpFun.Features.Retrieval.Entities;

namespace DndMcpAICsharpFun.Features.CharacterAdvice;

/// <summary>
/// Ownership-gated orchestrator for level-up advice: resolves the caller-owned snapshot, builds the
/// candidate class list (existing classes to advance, plus — when requested — legal multiclass dips),
/// grounds each candidate against the typed <c>dnd_entities</c> store, runs the deterministic
/// <see cref="LevelUpPlanner"/>, and fills every open choice with real cited options. A candidate whose
/// class entity cannot be found in the typed store is skipped rather than fabricated — the grounding
/// contract. An illegal multiclass dip (failing <see cref="MulticlassRules.CanMulticlassInto"/>) is
/// simply never added as a candidate; it is neither recommended nor silently invented.
/// </summary>
public sealed class LevelUpAdviceService(
    HeroRepository heroes,
    IEntityRetrievalService retrieval,
    LevelUpPlanner planner,
    EntityOptionProvider options)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<LevelUpAdvice> PlanForUserAsync(
        long heroSnapshotId, long userId, string? targetClass, bool considerDip, CancellationToken ct)
    {
        var snapshot = await heroes.GetSnapshotForUserAsync(heroSnapshotId, userId);
        if (snapshot is null)
            throw new UnauthorizedAccessException("Hero snapshot not found or not owned by the caller.");
        var sheet = snapshot.Sheet;

        var hero = await heroes.GetByIdAsync(snapshot.HeroId);
        var heroName = hero?.Name ?? "";

        var existingClassNames = sheet.Classes.Select(c => c.Class).ToList();

        var candidateClasses = new List<(string ClassName, bool IsDip)>();
        foreach (var existing in existingClassNames)
        {
            if (targetClass is null || string.Equals(existing, targetClass, StringComparison.OrdinalIgnoreCase))
                candidateClasses.Add((existing, false));
        }

        if (considerDip)
        {
            foreach (var known in MulticlassRules.KnownClasses)
            {
                if (targetClass is not null && !string.Equals(known, targetClass, StringComparison.OrdinalIgnoreCase))
                    continue; // targetClass scopes dip candidates too — no whole-roster ballooning
                if (existingClassNames.Any(c => string.Equals(c, known, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (!MulticlassRules.CanMulticlassInto(known, sheet).Allowed)
                    continue; // ineligible dips are excluded — never recommended, never fabricated
                candidateClasses.Add((known, true));
            }
        }

        var candidates = new List<AdvancementCandidate>();
        var edition = "";

        foreach (var (className, isDip) in candidateClasses)
        {
            var classResults = await retrieval.SearchDiagnosticAsync(
                new EntitySearchQuery(
                    QueryText: className,
                    Type: EntityType.Class,
                    SourceBook: null,
                    Edition: null,
                    BookType: null,
                    SettingTag: null,
                    Keyword: null,
                    CrNumericLte: null,
                    CrNumericGte: null,
                    SpellLevel: null,
                    DamageType: null,
                    TopK: 5),
                ct);
            var classEntity = classResults.FirstOrDefault(
                r => string.Equals(r.Name, className, StringComparison.OrdinalIgnoreCase));
            if (classEntity is null)
                continue; // exact class not in corpus — skip rather than ground on a wrong class (grounding contract)
            var classFields = classEntity.Fields.Deserialize<ClassFields>(JsonOpts);
            if (classFields is null)
                continue;
            var candidateEdition = classEntity.Edition;

            SubclassFields? currentSubclassFields = null;
            if (!isDip)
            {
                var currentSubclass = sheet.Classes
                    .FirstOrDefault(c => string.Equals(c.Class, className, StringComparison.OrdinalIgnoreCase))
                    ?.Subclass;
                if (!string.IsNullOrWhiteSpace(currentSubclass))
                {
                    var subclassResults = await retrieval.SearchDiagnosticAsync(
                        new EntitySearchQuery(
                            QueryText: currentSubclass,
                            Type: EntityType.Subclass,
                            SourceBook: null,
                            Edition: null,
                            BookType: null,
                            SettingTag: null,
                            Keyword: null,
                            CrNumericLte: null,
                            CrNumericGte: null,
                            SpellLevel: null,
                            DamageType: null,
                            TopK: 5),
                        ct);
                    var subclassEntity = subclassResults.FirstOrDefault(
                        r => string.Equals(r.Name, currentSubclass, StringComparison.OrdinalIgnoreCase));
                    currentSubclassFields = subclassEntity?.Fields.Deserialize<SubclassFields>(JsonOpts);
                }
            }

            var delta = planner.Plan(sheet, className, classFields, currentSubclassFields);

            var filledChoices = new List<OpenChoice>();
            foreach (var choice in delta.OpenChoices)
            {
                IReadOnlyList<CitedOption> filledOptions = choice.Kind switch
                {
                    OpenChoiceKind.Subclass => await options.SubclassOptions(className, candidateEdition, ct),
                    OpenChoiceKind.AbilityScoreOrFeat => await options.FeatOptions(candidateEdition, ct),
                    OpenChoiceKind.Spells => await options.SpellOptions(
                        className, HighestSlotLevel(delta.SpellSlotsAfter), candidateEdition, ct),
                    _ => choice.Options, // ClassSpecific — surfaced, not optimized
                };
                filledChoices.Add(choice with { Options = filledOptions });
            }

            var filledDelta = delta with { OpenChoices = filledChoices };
            var dipValidity = isDip ? new DipValidity(true, "met") : null;
            candidates.Add(new AdvancementCandidate(className, isDip, filledDelta, dipValidity));

            if (edition.Length == 0)
                edition = candidateEdition;
        }

        return new LevelUpAdvice(heroSnapshotId, heroName, edition, candidates);
    }

    private static int HighestSlotLevel(IReadOnlyList<int> slots)
    {
        for (var i = slots.Count - 1; i >= 0; i--)
            if (slots[i] > 0) return i + 1;
        return 0;
    }
}
