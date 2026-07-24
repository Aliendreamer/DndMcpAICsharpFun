using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Resolution;

namespace DndMcpAICsharpFun.Features.CharacterAdvice;

/// <summary>Pure, deterministic computation of "what changes at the next level" for advancing
/// <c>targetClass</c> by one level. Whether this is an existing-class advance or a new-class dip is
/// derived from <paramref name="sheet"/> — <c>targetClass</c> is a dip if it is not already present
/// in <c>sheet.Classes</c>, an advance otherwise. No I/O; option menus (<see cref="OpenChoice.Options"/>)
/// are left empty — they are filled by a later grounded-option-lookup step.</summary>
public sealed class LevelUpPlanner
{
    private static readonly string[] ClassSpecificMarkers =
        ["Eldritch Invocation", "Metamagic", "Maneuver", "Expertise", "Fighting Style"];

    public LevelUpDelta Plan(
        CharacterSheet sheet, string targetClass,
        ClassFields classFields, SubclassFields? currentSubclassFields)
    {
        var before = sheet.Classes.Select(c => new ClassLevel { Class = c.Class, Subclass = c.Subclass, Level = c.Level }).ToList();
        var existing = before.FirstOrDefault(c => string.Equals(c.Class, targetClass, StringComparison.OrdinalIgnoreCase));
        var currentClassLevel = existing?.Level ?? 0;
        var newClassLevel = currentClassLevel + 1;

        var after = before.Select(c => new ClassLevel { Class = c.Class, Subclass = c.Subclass, Level = c.Level }).ToList();
        var afterTarget = after.FirstOrDefault(c => string.Equals(c.Class, targetClass, StringComparison.OrdinalIgnoreCase));
        if (afterTarget is null) after.Add(new ClassLevel { Class = targetClass, Subclass = "", Level = 1 });
        else afterTarget.Level += 1;

        var newTotalLevel = sheet.Level + 1;
        var pbBefore = CharacterSheet.ProficiencyBonusForLevel(sheet.Level);
        var pbAfter = CharacterSheet.ProficiencyBonusForLevel(newTotalLevel);

        // HP: hit-die average (ceil((faces+1)/2), i.e. faces/2 + 1 via integer division) + CON modifier,
        // floored at the D&D minimum of 1 HP per level.
        var faces = classFields.Hd?.Faces ?? 8;
        var hpAverage = Math.Max(1, (faces / 2) + 1 + CharacterSheet.Modifier(sheet.Constitution));

        var slotsBefore = MulticlassSlotTableSeeder.SlotsForCasterLevel(MulticlassSpellcasting.ResolveSlotSource(before));
        var slotsAfter = MulticlassSlotTableSeeder.SlotsForCasterLevel(MulticlassSpellcasting.ResolveSlotSource(after));

        var classFeaturesAtLevel = ClassFeatureRefParser.Parse(classFields.ClassFeatures, "classFeature")
            .Where(f => f.Level == newClassLevel).ToList();
        var subclassFeaturesAtLevel = ClassFeatureRefParser.Parse(currentSubclassFields?.SubclassFeatures, "subclassFeature")
            .Where(f => f.Level == newClassLevel).ToList();

        var featuresGained = classFeaturesAtLevel.Concat(subclassFeaturesAtLevel)
            .Select(f => new FeatureGain(f.Name, f.Source)).ToList();

        var choices = new List<OpenChoice>();
        if (featuresGained.Any(f => f.Name.Contains("Ability Score Improvement", StringComparison.OrdinalIgnoreCase)))
            choices.Add(new OpenChoice(OpenChoiceKind.AbilityScoreOrFeat, "Ability Score Improvement or a feat", []));

        // Subclass selection = the earliest level this class grants a subclass feature (the "Martial Archetype"
        // / "Divine Domain" style marker), matched at newClassLevel.
        var subclassMarker = classFields.SubclassTitle;
        var isSubclassSelectionLevel = !string.IsNullOrWhiteSpace(subclassMarker)
            && classFeaturesAtLevel.Any(f => !string.IsNullOrWhiteSpace(subclassMarker)
                && f.Name.Contains(subclassMarker!, StringComparison.OrdinalIgnoreCase));
        if (isSubclassSelectionLevel)
            choices.Add(new OpenChoice(OpenChoiceKind.Subclass, $"Choose your {subclassMarker}", []));

        // Caster gained access to a new highest spell level → a Spells choice (options filled by the provider).
        var newHighestBefore = HighestSlotLevel(slotsBefore);
        var newHighestAfter = HighestSlotLevel(slotsAfter);
        if (newHighestAfter > newHighestBefore && newHighestAfter > 0)
            choices.Add(new OpenChoice(OpenChoiceKind.Spells, $"New level-{newHighestAfter} spells", []));

        foreach (var marker in ClassSpecificMarkers)
            if (featuresGained.Any(f => f.Name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                choices.Add(new OpenChoice(OpenChoiceKind.ClassSpecific, marker, []));

        return new LevelUpDelta(
            targetClass, newClassLevel, newTotalLevel, hpAverage, $"1d{faces}",
            pbBefore, pbAfter, slotsBefore, slotsAfter, featuresGained, choices, isSubclassSelectionLevel);
    }

    private static int HighestSlotLevel(IReadOnlyList<int> slots)
    {
        for (var i = slots.Count - 1; i >= 0; i--)
            if (slots[i] > 0) return i + 1;
        return 0;
    }
}