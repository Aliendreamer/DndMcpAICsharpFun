namespace DndMcpAICsharpFun.Features.CharacterAdvice;

/// <summary>A real entity offered as a choice — always carries provenance so nothing is invented.</summary>
public sealed record CitedOption(string Id, string Name, string Source);

public enum OpenChoiceKind { AbilityScoreOrFeat, Subclass, Spells, ClassSpecific }

/// <summary>An open decision the level unlocks, plus its real cited options. For ClassSpecific
/// (invocations/metamagic/etc.) Options may be empty — the choice is surfaced, not optimized.</summary>
public sealed record OpenChoice(OpenChoiceKind Kind, string Label, IReadOnlyList<CitedOption> Options);

public sealed record FeatureGain(string Name, string Source);

/// <summary>Rule-grounded "what changes" for advancing one class by one level.</summary>
public sealed record LevelUpDelta(
    string ClassName,
    int NewClassLevel,
    int NewTotalLevel,
    int HpAverageGain,
    string HpRollFormula,
    int ProficiencyBonusBefore,
    int ProficiencyBonusAfter,
    IReadOnlyList<int> SpellSlotsBefore,
    IReadOnlyList<int> SpellSlotsAfter,
    IReadOnlyList<FeatureGain> FeaturesGained,
    IReadOnlyList<OpenChoice> OpenChoices,
    bool IsSubclassSelectionLevel);

public sealed record DipValidity(bool Allowed, string Reason);

/// <summary>One way to advance: an existing class (+1) or a new-class dip (level 1).</summary>
public sealed record AdvancementCandidate(
    string ClassName,
    bool IsNewClassDip,
    LevelUpDelta Delta,
    DipValidity? DipValidity);

public sealed record LevelUpAdvice(
    long HeroSnapshotId,
    string HeroName,
    string Edition,
    IReadOnlyList<AdvancementCandidate> Candidates);
