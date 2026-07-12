using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Resolution;
using DndMcpAICsharpFun.Features.Retrieval.Entities;

namespace DndMcpAICsharpFun.Features.CharacterAdvice;

/// <summary>
/// Ownership-gated build critique: three grounded finding sets over a caller-owned hero snapshot.
/// (A) Untaken choices — needs the class entity (edition-pinned, like <see cref="LevelUpAdviceService"/>)
/// for <c>classFeatures</c> + <c>subclassTitle</c>: flags a class past its subclass-selection level with
/// no subclass recorded, and flags parsed class features not present (name-normalized) on the sheet.
/// (B) Stat consistency and (C) ability alignment are pure-sheet checks against shipped primitives —
/// no class-entity lookup. A class entity that can't be found in the typed store simply contributes no
/// (A) findings for that class, rather than fabricating one (the grounding contract).
/// </summary>
public sealed class BuildCritiqueService(HeroRepository heroes, IEntityRetrievalService retrieval)
{
    private const string BuildEdition = "Edition2014";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<BuildCritique> CritiqueForUserAsync(long heroSnapshotId, long userId, CancellationToken ct)
    {
        var snapshot = await heroes.GetSnapshotForUserAsync(heroSnapshotId, userId);
        if (snapshot is null)
            throw new UnauthorizedAccessException("Hero snapshot not found or not owned by the caller.");
        var sheet = snapshot.Sheet;

        var findings = new List<CritiqueFinding>();
        foreach (var cls in sheet.Classes)
            findings.AddRange(await UntakenChoicesAsync(sheet, cls, ct));   // (A)
        findings.AddRange(StatConsistency(sheet));                          // (B)
        findings.AddRange(AbilityAlignment(sheet));                        // (C)

        return new BuildCritique(heroSnapshotId, findings, []);
    }

    // (A) — needs the class entity for classFeatures + subclassTitle.
    private async Task<IReadOnlyList<CritiqueFinding>> UntakenChoicesAsync(
        CharacterSheet sheet, ClassLevel cls, CancellationToken ct)
    {
        var results = await retrieval.SearchDiagnosticAsync(
            new EntitySearchQuery(cls.Class, EntityType.Class, null, BuildEdition, null, null, null,
                null, null, null, null, 5), ct);
        var entity = results.FirstOrDefault(r =>
            string.Equals(r.Name, cls.Class, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Edition, BuildEdition, StringComparison.OrdinalIgnoreCase));
        if (entity is null) return [];
        var fields = entity.Fields.Deserialize<ClassFields>(JsonOpts);
        if (fields is null) return [];

        var feats = ClassFeatureRefParser.Parse(fields.ClassFeatures, "classFeature");
        var findings = new List<CritiqueFinding>();

        // subclass-not-chosen: earliest level whose feature name contains the subclass title.
        var title = fields.SubclassTitle;
        if (!string.IsNullOrWhiteSpace(title))
        {
            var subLevel = feats
                .Where(f => f.Name.Contains(title!, StringComparison.OrdinalIgnoreCase))
                .Select(f => (int?)f.Level).Min();
            if (subLevel is int sl && cls.Level >= sl && string.IsNullOrWhiteSpace(cls.Subclass))
                findings.Add(new(CritiqueKind.UntakenChoice,
                    $"Your {cls.Class} is level {cls.Level} but has no subclass ({title}) chosen (due at level {sl}).",
                    new CitedOption(entity.Id, entity.Name, entity.SourceBook)));
        }

        // missing recorded features up to the class level.
        var have = sheet.Features.Select(f => EntityNameIndex.Normalize(f.Name)).ToHashSet(StringComparer.Ordinal);
        foreach (var f in feats.Where(f => f.Level <= cls.Level))
        {
            if (title is not null && f.Name.Contains(title, StringComparison.OrdinalIgnoreCase)) continue; // the subclass slot itself
            if (!have.Contains(EntityNameIndex.Normalize(f.Name)))
                findings.Add(new(CritiqueKind.UntakenChoice,
                    $"A level-{f.Level} {cls.Class} gains \"{f.Name}\", which isn't recorded on your sheet.",
                    new CitedOption(entity.Id, entity.Name, entity.SourceBook)));
        }
        return findings;
    }

    // (B) — internal consistency of the recorded sheet, using shipped primitives. No class-entity lookup.
    private static IReadOnlyList<CritiqueFinding> StatConsistency(CharacterSheet sheet)
    {
        var findings = new List<CritiqueFinding>();
        var ability = sheet.SpellcastingAbility;                    // the sheet's recorded casting ability
        if (!string.IsNullOrWhiteSpace(ability) && AbilityScore(sheet, ability) is int score)
        {
            var mod = CharacterSheet.Modifier(score);
            var pb = sheet.ProficiencyBonus;
            var dc = 8 + pb + mod;
            if (sheet.SpellSaveDC != 0 && sheet.SpellSaveDC != dc)
                findings.Add(new(CritiqueKind.StatConsistency,
                    $"Your recorded spell save DC is {sheet.SpellSaveDC}, but {ability} {CharacterSheet.ModifierStr(score)} + proficiency +{pb} computes {dc}.", null));
            var atk = pb + mod;
            if (sheet.SpellAttackBonus != 0 && sheet.SpellAttackBonus != atk)
                findings.Add(new(CritiqueKind.StatConsistency,
                    $"Your recorded spell attack bonus is +{sheet.SpellAttackBonus}, but computes +{atk}.", null));
        }

        var computedSlots = MulticlassSlotTableSeeder.SlotsForCasterLevel(
            MulticlassSpellcasting.ResolveSlotSource(sheet.Classes));
        if (!SlotsMatch(sheet.SpellSlots, computedSlots))
            findings.Add(new(CritiqueKind.StatConsistency,
                "Your recorded spell slots don't match your caster level's slots.", null));
        return findings;
    }

    // (C) — highest ability vs each caster class's spellcasting ability.
    private static IReadOnlyList<CritiqueFinding> AbilityAlignment(CharacterSheet sheet)
    {
        var findings = new List<CritiqueFinding>();
        var (highName, highScore) = HighestAbility(sheet);
        foreach (var cls in sheet.Classes)
        {
            var castAbility = MulticlassSpellcasting.SpellcastingAbility(cls.Class);
            if (castAbility is null) continue;                       // non-caster
            if (!string.Equals(castAbility, highName, StringComparison.OrdinalIgnoreCase)
                && AbilityScore(sheet, castAbility) is int cs && cs < highScore)
                findings.Add(new(CritiqueKind.AbilityAlignment,
                    $"Your {cls.Class}'s spellcasting ability is {castAbility} ({cs}), but your highest score is {highName} ({highScore}).", null));
        }
        return findings;
    }

    private static int? AbilityScore(CharacterSheet s, string ability) => ability.ToLowerInvariant() switch
    {
        "strength" => s.Strength, "dexterity" => s.Dexterity, "constitution" => s.Constitution,
        "intelligence" => s.Intelligence, "wisdom" => s.Wisdom, "charisma" => s.Charisma, _ => null,
    };

    private static (string Name, int Score) HighestAbility(CharacterSheet s)
    {
        var all = new (string, int)[] { ("Strength", s.Strength), ("Dexterity", s.Dexterity),
            ("Constitution", s.Constitution), ("Intelligence", s.Intelligence),
            ("Wisdom", s.Wisdom), ("Charisma", s.Charisma) };
        return all.MaxBy(a => a.Item2);
    }

    private static bool SlotsMatch(int[] recorded, int[] computed)
    {
        for (var i = 0; i < 9; i++)
            if ((i < recorded.Length ? recorded[i] : 0) != (i < computed.Length ? computed[i] : 0)) return false;
        return true;
    }
}
