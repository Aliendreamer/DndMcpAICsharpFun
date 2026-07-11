# Character Level-Up Advice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A per-user, ownership-gated level-up assistant that computes a rule-grounded level-up delta + cited option menus for an owned hero (advancing an existing class or a new-class dip), exposed as a `plan_level_up` chat tool and a HeroDetail grounded card.

**Architecture:** Mirror the encounter surface's deterministic-core + LLM-reasoning split. A pure `LevelUpPlanner` computes the delta from class-entity rule data (Hd, the PB formula, the shipped PHB slot tables, parsed per-level features). `EntityOptionProvider` fetches real cited options (subclass/feat/spell) from `dnd_entities`. `LevelUpAdviceService.PlanForUserAsync` resolves the owned snapshot (or throws) and assembles per-candidate `{delta + options + dip validity}`. Two surfaces read it: a per-user in-process chat tool (the LLM recommends from the returned cited menu) and a display-only HeroDetail card that hands off to chat.

**Tech Stack:** .NET 10, ASP.NET Core, Blazor Server (`@rendermode InteractiveServer`), EF Core/Npgsql, Qdrant (`dnd_entities`), `Microsoft.Extensions.AI` chat tools, xUnit + FluentAssertions + Testcontainers (Postgres + Qdrant).

## Global Constraints

- **Serena for ALL `.cs`/`.razor` reads and edits** — built-in Read/Edit/Write on code files is forbidden (project rule); Razor edits use Serena `replace_content` / `create_text_file` (no LSP symbols for Razor).
- **Warnings-as-errors** (`Directory.Build.props`) — every build is 0 warnings / 0 errors.
- `dotnet build` / `dotnet test` run under `dangerouslyDisableSandbox: true` (Config is git-crypted); the full suite needs **Docker** (Testcontainers Postgres + Qdrant). Use a generous timeout (600000 ms).
- **No new HTTP route or MCP-server tool** (per-user in-process chat tool + server-side Blazor) → **no `DndMcpAICsharpFun.http` / `dnd-mcp-api.insomnia.json` change.**
- **Grounding contract:** every option the advice names (feat/subclass/spell) is a real `dnd_entities` record carrying `id + name + source`; the deterministic delta comes only from class-entity rule data. The chat-tool description instructs the model to recommend strictly from the returned options.
- **Ownership:** the orchestrator is `*ForUserAsync` only; it resolves the snapshot via `HeroRepository.GetSnapshotForUserAsync(snapshotId, userId)` (null → throw). The chat tool closes over the session `userId`, never a tool argument.
- **A new `Add*` DI extension is registered in TWO places** — `Program.cs`/its composition and the `FullContainerScopeValidationTests.BuildServiceCollection` replica — and the FINAL verify runs the **full** `dotnet test`, never a feature-filtered subset.
- Reuse verbatim: `MulticlassSpellcasting.ResolveSlotSource(classes)`, `MulticlassSlotTableSeeder` PHB slot arrays, `CharacterSheet.ProficiencyBonusForLevel`, `CharacterResolutionService.ResolveMulticlassValidity(sheet, targetClass)`, `MulticlassRules.KnownClasses`/`CanMulticlassInto`, `IEntityRetrievalService`/`EntitySearchQuery`, `HeroRepository.GetSnapshotForUserAsync`, `QdrantFixture`.

---

## File Structure

- **Create** `Features/CharacterAdvice/LevelUpModels.cs` — records/enum: `CitedOption`, `OpenChoiceKind`, `OpenChoice`, `FeatureGain`, `LevelUpDelta`, `DipValidity`, `AdvancementCandidate`, `LevelUpAdvice`.
- **Create** `Features/CharacterAdvice/ClassFeatureRefParser.cs` — parse 5etools `classFeatures`/`subclassFeatures` `JsonElement`s → `FeatureRef(Name, Source, Level)`.
- **Create** `Features/CharacterAdvice/LevelUpPlanner.cs` — pure deterministic `LevelUpDelta` computation.
- **Create** `Features/CharacterAdvice/EntityOptionProvider.cs` — cited subclass/feat/spell option queries over `dnd_entities`.
- **Create** `Features/CharacterAdvice/LevelUpAdviceService.cs` — ownership-gated orchestrator.
- **Create** `Features/CharacterAdvice/CharacterAdviceServiceCollectionExtensions.cs` — `AddCharacterAdvice()`.
- **Modify** `Features/Resolution/MulticlassSlotTableSeeder.cs` — add a public static `SlotsForCasterLevel(SlotSource)` reader over the existing PHB arrays.
- **Modify** `Features/Chat/DndChatService.cs` — ctor dep + `plan_level_up` tool; wire `AddCharacterAdvice()` into `AddDndChat`.
- **Modify** `CompanionUI/Pages/Campaigns/HeroDetail.razor` — "Plan level-up" grounded card + chat hand-off.
- **Modify** `CompanionUI/Pages/Chat.razor` — read a `?prompt=` query param to seed the input.
- **Modify** `Program.cs` composition + `DndMcpAICsharpFun.Tests/**/FullContainerScopeValidationTests.cs` replica — register `AddCharacterAdvice()`.
- **Test** `DndMcpAICsharpFun.Tests/CharacterAdvice/…` — `ClassFeatureRefParserTests`, `SlotTableReaderTests`, `LevelUpPlannerTests`, `EntityOptionProviderIntegrationTests`, `LevelUpAdviceServiceTests`.

---

### Task 1: PHB slot-table public reader

**Files:**
- Modify: `Features/Resolution/MulticlassSlotTableSeeder.cs`
- Test: `DndMcpAICsharpFun.Tests/CharacterAdvice/SlotTableReaderTests.cs`

**Interfaces:**
- Consumes: `MulticlassSpellcasting.SlotSource(string Kind, int Level)` (existing); the private PHB arrays `Slots`/`HalfCasterSlots`/`ThirdCasterSlots` (row i = combined caster level i+1, 9 columns).
- Produces: `public static int[] MulticlassSlotTableSeeder.SlotsForCasterLevel(MulticlassSpellcasting.SlotSource src)` → a length-9 array of per-spell-level slot counts (all zeros for `Kind=="none"`, `Level<1`, or `Level>20`).

**Context:** The slot tables already live as in-memory arrays in `MulticlassSlotTableSeeder`. The planner (Task 4) diffs slots at level N vs N+1 without a DB read, so expose a pure reader over those arrays keyed by `SlotSource`.

- [ ] **Step 1: Write the failing test**

```csharp
// DndMcpAICsharpFun.Tests/CharacterAdvice/SlotTableReaderTests.cs
using DndMcpAICsharpFun.Features.Resolution;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.CharacterAdvice;

public class SlotTableReaderTests
{
    [Fact]
    public void FullCaster_level3_hasTwoFirstAndTwoSecond()
    {
        var slots = MulticlassSlotTableSeeder.SlotsForCasterLevel(new("multiclass", 3));
        slots.Should().Equal(4, 2, 0, 0, 0, 0, 0, 0, 0);
    }

    [Fact]
    public void HalfCaster_level1_hasNoSlots()
    {
        MulticlassSlotTableSeeder.SlotsForCasterLevel(new("half", 1))
            .Should().OnlyContain(x => x == 0);
    }

    [Fact]
    public void None_orOutOfRange_isAllZero()
    {
        MulticlassSlotTableSeeder.SlotsForCasterLevel(new("none", 0)).Should().OnlyContain(x => x == 0);
        MulticlassSlotTableSeeder.SlotsForCasterLevel(new("multiclass", 25)).Should().OnlyContain(x => x == 0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~SlotTableReaderTests"` (with `dangerouslyDisableSandbox: true`)
Expected: FAIL — `SlotsForCasterLevel` does not exist.

- [ ] **Step 3: Add the reader (Serena `replace_content` — insert a method into the class)**

```csharp
    /// <summary>Per-spell-level (1..9) slot counts for a resolved caster source, or all-zero
    /// for a non-caster / out-of-range level. Reads the same PHB arrays this class seeds.</summary>
    public static int[] SlotsForCasterLevel(MulticlassSpellcasting.SlotSource src)
    {
        var table = src.Kind switch
        {
            "half" => HalfCasterSlots,
            "third" => ThirdCasterSlots,
            "multiclass" => Slots,
            _ => null,
        };
        if (table is null || src.Level < 1 || src.Level > table.Length)
            return new int[9];
        return (int[])table[src.Level - 1].Clone();
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~SlotTableReaderTests"`
Expected: PASS (3/3).

- [ ] **Step 5: Commit**

```bash
git add Features/Resolution/MulticlassSlotTableSeeder.cs DndMcpAICsharpFun.Tests/CharacterAdvice/SlotTableReaderTests.cs
git commit -m "feat(level-up): PHB slot-table public reader"
```

---

### Task 2: Class-feature ref parser

**Files:**
- Create: `Features/CharacterAdvice/ClassFeatureRefParser.cs`
- Test: `DndMcpAICsharpFun.Tests/CharacterAdvice/ClassFeatureRefParserTests.cs`

**Interfaces:**
- Consumes: `ClassFields.ClassFeatures` / `SubclassFields.SubclassFeatures` (`IReadOnlyList<JsonElement>?`) — 5etools refs, each either a string `"Name|Class|Source|Level"` or an object `{ "classFeature": "Name|Class|Source|Level" }` (subclass variant uses `"subclassFeature"`).
- Produces: `public static IReadOnlyList<FeatureRef> ClassFeatureRefParser.Parse(IReadOnlyList<JsonElement>? refs, string key)` where `key` is `"classFeature"` or `"subclassFeature"`; `public sealed record FeatureRef(string Name, string Source, int Level)`. Unparseable entries are skipped (never guessed).

**Context:** 5etools `classFeatures` entries name a feature and the level it is gained; the detail text lives elsewhere. We only need `(name, source, level)` to list what a level grants and to detect choice-points.

- [ ] **Step 1: Write the failing test**

```csharp
// DndMcpAICsharpFun.Tests/CharacterAdvice/ClassFeatureRefParserTests.cs
using System.Text.Json;
using DndMcpAICsharpFun.Features.CharacterAdvice;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.CharacterAdvice;

public class ClassFeatureRefParserTests
{
    private static IReadOnlyList<JsonElement> Json(string arrayJson) =>
        JsonSerializer.Deserialize<List<JsonElement>>(arrayJson)!;

    [Fact]
    public void ParsesStringAndObjectRefs_withLevel()
    {
        var refs = Json("""
            [ "Action Surge|Fighter|PHB|2",
              { "classFeature": "Extra Attack|Fighter|PHB|5" } ]
            """);
        var parsed = ClassFeatureRefParser.Parse(refs, "classFeature");
        parsed.Should().BeEquivalentTo(new[]
        {
            new FeatureRef("Action Surge", "PHB", 2),
            new FeatureRef("Extra Attack", "PHB", 5),
        });
    }

    [Fact]
    public void SkipsMalformedRefs()
    {
        var refs = Json("""[ "no-pipes-here", { "other": "x" }, 42 ]""");
        ClassFeatureRefParser.Parse(refs, "classFeature").Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ClassFeatureRefParserTests"`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement the parser**

```csharp
// Features/CharacterAdvice/ClassFeatureRefParser.cs
using System.Text.Json;

namespace DndMcpAICsharpFun.Features.CharacterAdvice;

public sealed record FeatureRef(string Name, string Source, int Level);

/// <summary>Parses 5etools classFeatures/subclassFeatures refs ("Name|Class|Source|Level",
/// string or { key: "..." } object) into (name, source, level). Malformed refs are skipped.</summary>
public static class ClassFeatureRefParser
{
    public static IReadOnlyList<FeatureRef> Parse(IReadOnlyList<JsonElement>? refs, string key)
    {
        if (refs is null) return [];
        var result = new List<FeatureRef>();
        foreach (var el in refs)
        {
            var raw = el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Object when el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String
                    => p.GetString(),
                _ => null,
            };
            if (raw is null) continue;

            var parts = raw.Split('|');
            if (parts.Length < 4) continue;
            if (!int.TryParse(parts[3].Trim(), out var level)) continue;
            var name = parts[0].Trim();
            var source = parts[2].Trim();
            if (name.Length == 0) continue;
            result.Add(new FeatureRef(name, source, level));
        }
        return result;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ClassFeatureRefParserTests"`
Expected: PASS (2/2).

- [ ] **Step 5: Commit**

```bash
git add Features/CharacterAdvice/ClassFeatureRefParser.cs DndMcpAICsharpFun.Tests/CharacterAdvice/ClassFeatureRefParserTests.cs
git commit -m "feat(level-up): 5etools class-feature ref parser"
```

---

### Task 3: Level-up models

**Files:**
- Create: `Features/CharacterAdvice/LevelUpModels.cs`

**Interfaces:**
- Produces (consumed by Tasks 4–8):

```csharp
// Features/CharacterAdvice/LevelUpModels.cs
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
```

- [ ] **Step 1: Create the file** with the exact content above.
- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add Features/CharacterAdvice/LevelUpModels.cs
git commit -m "feat(level-up): advice model records"
```

---

### Task 4: Deterministic LevelUpPlanner

**Files:**
- Create: `Features/CharacterAdvice/LevelUpPlanner.cs`
- Test: `DndMcpAICsharpFun.Tests/CharacterAdvice/LevelUpPlannerTests.cs`

**Interfaces:**
- Consumes: `CharacterSheet` (`Classes`, ability scores, `ProficiencyBonusForLevel`, `Modifier`), `ClassLevel`, `ClassFields` (`Hd`, `ClassFeatures`, `SubclassTitle`), `SubclassFields` (`SubclassFeatures`), `HitDice` (`SharedTypes.cs` — has a `Faces`/number field; confirm the property name with Serena and use it), `MulticlassSpellcasting.ResolveSlotSource`, `MulticlassSlotTableSeeder.SlotsForCasterLevel` (Task 1), `ClassFeatureRefParser` (Task 2), `CitedOption`/`OpenChoice`/`LevelUpDelta` (Task 3).
- Produces: `public LevelUpDelta LevelUpPlanner.Plan(CharacterSheet sheet, string targetClass, bool isNewClassDip, ClassFields classFields, SubclassFields? currentSubclassFields)`. Pure — no I/O; option menus are filled by Task 6, so `OpenChoice.Options` is left empty here (the planner only decides WHICH choices are open).

**Context:** The planner computes the delta for advancing `targetClass` by one level (or taking its first level when `isNewClassDip`). HP = hit-die average (`⌈(faces+1)/2⌉`) + CON modifier. PB from the *new total level*. Spell-slot diff = `SlotsForCasterLevel(ResolveSlotSource(after))` minus `…(before)`, where `after` is the classes list with the target advanced/added. Features gained = `classFeatures` (+ `subclassFeatures` for the character's existing subclass) filtered to the new class level. Choice detection: an `AbilityScoreOrFeat` choice when a gained feature name contains "Ability Score Improvement"; a `Subclass` choice when the class first grants its subclass feature at this level (the earliest level in `subclassFeatures`, or the class's `subclassTitle` marker in `classFeatures`); a `ClassSpecific` choice (Options empty) when a gained feature is a known choose-one feature (Eldritch Invocations, Metamagic, Maneuvers, Expertise, Fighting Style) — surfaced, not optimized.

- [ ] **Step 1: Write the failing tests**

```csharp
// DndMcpAICsharpFun.Tests/CharacterAdvice/LevelUpPlannerTests.cs
using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.CharacterAdvice;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.CharacterAdvice;

public class LevelUpPlannerTests
{
    private static ClassFields FighterFields() => new(
        Hd: new HitDice(Number: 1, Faces: 10),                     // confirm HitDice ctor shape via Serena
        Proficiency: ["con", "str"],
        StartingProficiencies: null,
        ClassFeatures: JsonSerializer.Deserialize<List<JsonElement>>("""
            [ "Fighting Style|Fighter|PHB|1", "Second Wind|Fighter|PHB|1",
              "Action Surge|Fighter|PHB|2",
              "Martial Archetype|Fighter|PHB|3",
              "Ability Score Improvement|Fighter|PHB|4",
              "Extra Attack|Fighter|PHB|5" ]
            """),
        Multiclassing: null,
        Entries: null,
        SubclassTitle: "Martial Archetype");

    private static CharacterSheet FighterAt(int level)
    {
        var s = new CharacterSheet { Constitution = 14 };            // +2 CON
        s.SetSingleClass("Fighter", "", level);
        return s;
    }

    [Fact]
    public void Advancing_fighter_4to5_gainsHpPbAndExtraAttack()
    {
        var delta = new LevelUpPlanner().Plan(FighterAt(4), "Fighter", false, FighterFields(), null);

        delta.NewClassLevel.Should().Be(5);
        delta.NewTotalLevel.Should().Be(5);
        delta.HpAverageGain.Should().Be(6 + 2);                       // d10 avg 6 (⌈11/2⌉) + CON 2
        delta.ProficiencyBonusBefore.Should().Be(2);
        delta.ProficiencyBonusAfter.Should().Be(3);                   // level 5 → +3
        delta.FeaturesGained.Select(f => f.Name).Should().Contain("Extra Attack");
    }

    [Fact]
    public void Level4_opensAbilityScoreOrFeatChoice()
    {
        var delta = new LevelUpPlanner().Plan(FighterAt(3), "Fighter", false, FighterFields(), null);
        delta.OpenChoices.Select(c => c.Kind).Should().Contain(OpenChoiceKind.AbilityScoreOrFeat);
    }

    [Fact]
    public void Level3_opensSubclassSelection()
    {
        var delta = new LevelUpPlanner().Plan(FighterAt(2), "Fighter", false, FighterFields(), null);
        delta.IsSubclassSelectionLevel.Should().BeTrue();
        delta.OpenChoices.Select(c => c.Kind).Should().Contain(OpenChoiceKind.Subclass);
    }

    [Fact]
    public void NonCaster_hasNoSlotChange()
    {
        var delta = new LevelUpPlanner().Plan(FighterAt(4), "Fighter", false, FighterFields(), null);
        delta.SpellSlotsBefore.Should().OnlyContain(x => x == 0);
        delta.SpellSlotsAfter.Should().OnlyContain(x => x == 0);
    }
}
```

> Before writing the impl, confirm the `HitDice` record's real ctor/property names in `Domain/Entities/Fields/SharedTypes.cs` with Serena and adjust `FighterFields()` + the impl's HP read to match (the average is `⌈(faces+1)/2⌉`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~LevelUpPlannerTests"`
Expected: FAIL — `LevelUpPlanner` does not exist.

- [ ] **Step 3: Implement `LevelUpPlanner`**

```csharp
// Features/CharacterAdvice/LevelUpPlanner.cs
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Resolution;

namespace DndMcpAICsharpFun.Features.CharacterAdvice;

public sealed class LevelUpPlanner
{
    private static readonly string[] ClassSpecificMarkers =
        ["Eldritch Invocation", "Metamagic", "Maneuver", "Expertise", "Fighting Style"];

    public LevelUpDelta Plan(
        CharacterSheet sheet, string targetClass, bool isNewClassDip,
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

        // HP: hit-die average + CON modifier. HitDice faces read below — confirm property name via Serena.
        var faces = classFields.Hd?.Faces ?? 8;
        var hpAverage = (faces / 2) + 1 + CharacterSheet.Modifier(sheet.Constitution);

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
            choices.Add(new OpenChoice(OpenChoiceKind.Subclass, $"Choose a {subclassMarker}", []));

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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~LevelUpPlannerTests"`
Expected: PASS. (Fix the `HitDice`/`SubclassTitle` property names against the real records if the build flags them.)

- [ ] **Step 5: Build + commit**

Run: `dotnet build` → 0/0.

```bash
git add Features/CharacterAdvice/LevelUpPlanner.cs DndMcpAICsharpFun.Tests/CharacterAdvice/LevelUpPlannerTests.cs
git commit -m "feat(level-up): deterministic LevelUpPlanner (HP/PB/slots/features/choices)"
```

---

### Task 5: EntityOptionProvider (cited option menus)

**Files:**
- Create: `Features/CharacterAdvice/EntityOptionProvider.cs`
- Test: `DndMcpAICsharpFun.Tests/CharacterAdvice/EntityOptionProviderIntegrationTests.cs`

**Interfaces:**
- Consumes: `IEntityRetrievalService.SearchAsync(EntitySearchQuery, ct)` → `IList<EntitySearchResult>` (`Id, Type, Name, SourceBook, …`); `SearchDiagnosticAsync` → `IList<EntityDiagnosticResult>` (adds `Fields` JsonElement); `EntityType` (`Subclass`/`Feat`/`Spell`); `SubclassFields.ClassName`; `CitedOption` (Task 3).
- Produces:
  - `Task<IReadOnlyList<CitedOption>> SubclassOptions(string className, string edition, CancellationToken ct)`
  - `Task<IReadOnlyList<CitedOption>> FeatOptions(string edition, CancellationToken ct)`
  - `Task<IReadOnlyList<CitedOption>> SpellOptions(string className, int spellLevel, string edition, CancellationToken ct)`

**Context:** Options are real typed queries. Subclasses have no `className` query field, so query `Type=Subclass` by class name text and post-filter on `SubclassFields.ClassName` (needs `SearchDiagnosticAsync` for `Fields`). Feats query `Type=Feat` + edition. Spells query `Type=Spell` + `SpellLevel` + edition (+ class-name text as the query); a class-list post-filter is a later refinement — slice 1 returns the level's spells for the edition. Each result projects to `new CitedOption(r.Id, r.Name, r.SourceBook)`. Use a reasonable `TopK` (e.g. 25) — and `log()`/comment that the menu is a top-K sample, not exhaustive.

- [ ] **Step 1: Write the failing integration test** (real Qdrant via `QdrantFixture`)

```csharp
// DndMcpAICsharpFun.Tests/CharacterAdvice/EntityOptionProviderIntegrationTests.cs
// Follow the existing QdrantFixture integration-test pattern (GUID-suffixed collection, seed
// entities, assert, clean up). See VectorStore/Entities/*IntegrationTests for the seeding helpers.
[Collection("qdrant")]
public class EntityOptionProviderIntegrationTests
{
    // Arrange: seed dnd_entities with 2 Fighter subclasses (SubclassFields.ClassName="Fighter"),
    // 1 Wizard subclass, and 2 feats, all edition "2014".
    // Act: SubclassOptions("Fighter","2014") ; FeatOptions("2014").
    // Assert: subclasses = exactly the 2 Fighter ones (Wizard one filtered out), each with Id/Name/Source;
    //         feats include the 2 seeded, each cited.
    // Non-vacuity: SubclassOptions("Wizard","2014") returns the Wizard one, not the Fighter ones.
}
```

> Fill this in against the real `QdrantFixture`/seeding helpers (read `VectorStore/Entities/QdrantFixture.cs` and an existing `*IntegrationTests` with Serena for the exact seed API). Assert the class-name post-filter genuinely excludes other classes' subclasses (the dev-flow non-vacuity rule).

- [ ] **Step 2: Run to verify it fails** — `dotnet test --filter "FullyQualifiedName~EntityOptionProviderIntegrationTests"` → FAIL (type missing). Docker must be up.

- [ ] **Step 3: Implement the provider**

```csharp
// Features/CharacterAdvice/EntityOptionProvider.cs
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Retrieval.Entities;

namespace DndMcpAICsharpFun.Features.CharacterAdvice;

public sealed class EntityOptionProvider(IEntityRetrievalService retrieval)
{
    private const int TopK = 25;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<CitedOption>> SubclassOptions(string className, string edition, CancellationToken ct)
    {
        var results = await retrieval.SearchDiagnosticAsync(
            new EntitySearchQuery(className, EntityType.Subclass, null, edition, null, null, null,
                null, null, null, null, TopK), ct);
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

    public async Task<IReadOnlyList<CitedOption>> FeatOptions(string edition, CancellationToken ct)
    {
        var results = await retrieval.SearchAsync(
            new EntitySearchQuery("feat", EntityType.Feat, null, edition, null, null, null,
                null, null, null, null, TopK), ct);
        return results.Select(r => new CitedOption(r.Id, r.Name, r.SourceBook)).ToList();
    }

    public async Task<IReadOnlyList<CitedOption>> SpellOptions(string className, int spellLevel, string edition, CancellationToken ct)
    {
        var results = await retrieval.SearchAsync(
            new EntitySearchQuery(className, EntityType.Spell, null, edition, null, null, null,
                null, spellLevel, null, TopK), ct);
        return results.Select(r => new CitedOption(r.Id, r.Name, r.SourceBook)).ToList();
    }
}
```

> Confirm the `EntitySearchQuery` positional args against the real record (QueryText, Type, SourceBook, Edition, BookType, SettingTag, Keyword, CrNumericLte, CrNumericGte, SpellLevel, DamageType, TopK) with Serena and align each call — get the `SpellLevel`/`TopK` positions right.

- [ ] **Step 4: Run to verify it passes** — `dotnet test --filter "FullyQualifiedName~EntityOptionProviderIntegrationTests"` → PASS.

- [ ] **Step 5: Commit**

```bash
git add Features/CharacterAdvice/EntityOptionProvider.cs DndMcpAICsharpFun.Tests/CharacterAdvice/EntityOptionProviderIntegrationTests.cs
git commit -m "feat(level-up): cited subclass/feat/spell option providers over dnd_entities"
```

---

### Task 6: LevelUpAdviceService (ownership-gated orchestrator) + DI

**Files:**
- Create: `Features/CharacterAdvice/LevelUpAdviceService.cs`
- Create: `Features/CharacterAdvice/CharacterAdviceServiceCollectionExtensions.cs`
- Modify: `Program.cs` composition (or the relevant `Add*` group) + the `FullContainerScopeValidationTests` replica
- Test: `DndMcpAICsharpFun.Tests/CharacterAdvice/LevelUpAdviceServiceTests.cs`

**Interfaces:**
- Consumes: `HeroRepository.GetSnapshotForUserAsync(long snapshotId, long userId)` → `HeroSnapshot?` (null → not owned); `HeroSnapshot.Sheet` (`CharacterSheet`), `.Level`; `LevelUpPlanner` (Task 4); `EntityOptionProvider` (Task 5); `IEntityRetrievalService` (fetch the class/subclass entity `Fields`); `MulticlassRules.KnownClasses` / `CanMulticlassInto(targetClass, sheet)`; `CharacterResolutionService.ResolveMulticlassValidity(sheet, targetClass)`; `EntityType.Class`/`Subclass`.
- Produces: `Task<LevelUpAdvice> PlanForUserAsync(long heroSnapshotId, long userId, string? targetClass, bool considerDip, CancellationToken ct)`.

**Context:** Resolve the owned snapshot or throw `UnauthorizedAccessException` (verbatim message from `ResolveForUserAsync`). Candidates = each existing class matching `targetClass` (or all when null), plus — when `considerDip` — each `MulticlassRules.KnownClasses` the hero doesn't have and is *eligible* for (`CanMulticlassInto(...).Allowed`), so Qdrant work is bounded to legal dips; an eligible dip carries a `DipValidity(true, "met")`. For each candidate: fetch the class entity (`SearchAsync Type=Class`, name+edition, take the best match) → deserialize `ClassFields`; fetch the character's current subclass entity for that class if present → `SubclassFields`; call `LevelUpPlanner.Plan`; then fill each `OpenChoice.Options` from `EntityOptionProvider` by kind (Subclass→`SubclassOptions`, AbilityScoreOrFeat→`FeatOptions`, Spells→`SpellOptions` at the new highest level). Edition = the class entity's `Edition`.

- [ ] **Step 1: Write the failing tests** (a Postgres-fixture test for ownership; a unit-ish test for dip validity)

```csharp
// DndMcpAICsharpFun.Tests/CharacterAdvice/LevelUpAdviceServiceTests.cs
// Uses PostgresFixture (real Postgres) to seed two users each owning a hero+snapshot, exactly like
// ResolveCharacterFeatureToolTests. Entity retrieval can be a fake IEntityRetrievalService returning
// a canned Fighter ClassFields + empty option lists (the ownership + dip-validity logic is what's under test).

[Fact] // ownership negative — SHIP BLOCKER
public async Task PlanForUser_otherUsersSnapshot_throws()
{
    // Arrange: user A owns snapshot S. Act/Assert:
    // (await service.PlanForUserAsync(S, userB, null, false, ct)).Should be UnauthorizedAccessException.
    await FluentActions.Awaiting(() => service.PlanForUserAsync(snapshotId, otherUserId, null, false, default))
        .Should().ThrowAsync<UnauthorizedAccessException>();
}

[Fact]
public async Task PlanForUser_ownerGetsCandidateForEachClass()
{
    var advice = await service.PlanForUserAsync(snapshotId, ownerUserId, null, false, default);
    advice.Candidates.Should().Contain(c => c.ClassName == "Fighter" && !c.IsNewClassDip);
}

[Fact]
public async Task ConsiderDip_illegalDip_isMarkedNotAllowed_notOmittedSilently()
{
    // A hero failing a class's ability prereq: that dip is either excluded from candidates OR present
    // with DipValidity.Allowed == false and a Reason — never recommended. Assert the chosen contract.
}
```

> Read `ResolveCharacterFeatureToolTests` with Serena for the exact `PostgresFixture` seeding of user→hero→snapshot and reuse it.

- [ ] **Step 2: Run to verify they fail** — `dotnet test --filter "FullyQualifiedName~LevelUpAdviceServiceTests"` → FAIL (type missing). Docker up.

- [ ] **Step 3: Implement `LevelUpAdviceService`** (per the Context above) and `AddCharacterAdvice()`:

```csharp
// Features/CharacterAdvice/CharacterAdviceServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Features.CharacterAdvice;

public static class CharacterAdviceServiceCollectionExtensions
{
    public static IServiceCollection AddCharacterAdvice(this IServiceCollection services)
    {
        services.AddScoped<LevelUpPlanner>();
        services.AddScoped<EntityOptionProvider>();
        services.AddScoped<LevelUpAdviceService>();
        return services;
    }
}
```

Match the lifetime to `EncounterDesignService`/`CharacterResolutionService` (read their registration with Serena; use the same scope). `LevelUpAdviceService` ctor: `(HeroRepository heroes, IEntityRetrievalService retrieval, LevelUpPlanner planner, EntityOptionProvider options)`.

- [ ] **Step 4: Register in BOTH places** — add `services.AddCharacterAdvice();` to `Program.cs`'s composition in the correct order, AND to `FullContainerScopeValidationTests.BuildServiceCollection` (it hand-replicates Program's `Add*` list; miss it and the scope gate passes vacuously). Grep both with Serena.

- [ ] **Step 5: Run tests + FULL suite**

Run: `dotnet test --filter "FullyQualifiedName~LevelUpAdviceServiceTests"` → PASS.
Run: `dotnet test` → all green (Docker up) — the container-scope validation now includes the new services.

- [ ] **Step 6: Commit**

```bash
git add Features/CharacterAdvice/LevelUpAdviceService.cs Features/CharacterAdvice/CharacterAdviceServiceCollectionExtensions.cs Program.cs DndMcpAICsharpFun.Tests/**/FullContainerScopeValidationTests.cs DndMcpAICsharpFun.Tests/CharacterAdvice/LevelUpAdviceServiceTests.cs
git commit -m "feat(level-up): ownership-gated LevelUpAdviceService + DI wiring"
```

---

### Task 7: `plan_level_up` chat tool

**Files:**
- Modify: `Features/Chat/DndChatService.cs`

**Interfaces:**
- Consumes: `LevelUpAdviceService.PlanForUserAsync` (Task 6); the existing per-user-tool pattern in `SendAsync` (`AIFunctionFactory.Create`, closure over `userId`).
- Produces: a `plan_level_up(heroSnapshotId, targetClass?, considerDip?)` tool in the authenticated tool list; `AddDndChat` pulls in `AddCharacterAdvice()`.

- [ ] **Step 1: Add the ctor dependency** — add `LevelUpAdviceService levelUpService` to the `DndChatService` primary constructor (after `encounterService`).

- [ ] **Step 2: Register the tool** inside the `if (long.TryParse(idClaim, out var userId))` block in `SendAsync`, alongside the existing tools:

```csharp
            toolList.Add(AIFunctionFactory.Create(
                (long heroSnapshotId, string? targetClass, bool? considerDip, CancellationToken toolCt) =>
                    levelUpService.PlanForUserAsync(
                        heroSnapshotId, userId, targetClass, considerDip ?? false, toolCt),
                name: "plan_level_up",
                description: "Plan the next level-up for a hero snapshot the signed-in user owns. Returns, " +
                    "for each way to advance (each existing class, plus legal new-class dips when considerDip " +
                    "is true), the rule-grounded delta (HP, proficiency bonus, spell-slot change, features " +
                    "gained) and the real cited options for each open choice (ability-score-or-feat, subclass, " +
                    "new spells). RECOMMEND a specific pick with reasons that reference the character's own " +
                    "sheet, but ONLY from the options returned here — never invent a feat, subclass, or spell. " +
                    "targetClass (optional) limits advice to one existing class."));
```

- [ ] **Step 3: Wire the dependency into `AddDndChat`** — make `AddDndChat` call `services.AddCharacterAdvice();` (so no composition can register `DndChatService` without its new dep), mirroring how `AddDndChat` pulls in `AddEncounters`. Grep `AddDndChat` with Serena.

- [ ] **Step 4: Build + full suite**

Run: `dotnet build` → 0/0. Run: `dotnet test` → green (the container-scope test exercises `DndChatService`'s dep graph).

- [ ] **Step 5: Commit**

```bash
git add Features/Chat/DndChatService.cs
git commit -m "feat(level-up): plan_level_up per-user chat tool"
```

---

### Task 8: HeroDetail grounded card + chat hand-off

**Files:**
- Modify: `CompanionUI/Pages/Campaigns/HeroDetail.razor`
- Modify: `CompanionUI/Pages/Chat.razor`

**Interfaces:**
- Consumes: `LevelUpAdviceService.PlanForUserAsync` (Task 6); `_hero.LatestSnapshot` (`.Id`, `.Sheet`); the `_userId` claim (currently a local in `OnInitializedAsync` — promote to a field).
- Produces: a display-only "Plan level-up" card on HeroDetail and a `?prompt=` seed on Chat.

**Context:** HeroDetail already computes `userId` from the `NameIdentifier` claim in `OnInitializedAsync` and loads `_hero`. Add `@inject LevelUpAdviceService LevelUpSvc`, store `_userId` as a field, and add a card that on click calls `LevelUpSvc.PlanForUserAsync(_hero.LatestSnapshot!.Id, _userId, targetClass: null, considerDip: true, default)` and renders the deterministic delta (HP, PB before→after, slot change, features gained, and the open choices) — NO LLM. Below it, an "Ask the assistant to recommend →" link that navigates to `/?prompt=<url-encoded prompt>` (e.g. `Plan my level-up for hero snapshot {id} ({name}).`). `Chat.razor` reads the `prompt` query param on init and seeds its input textbox (does NOT auto-send).

- [ ] **Step 1: HeroDetail — inject + field + card.** Add `@inject LevelUpAdviceService LevelUpSvc` and `@using DndMcpAICsharpFun.Features.CharacterAdvice`. Promote `_userId` to a field set in `OnInitializedAsync`. Add a `LevelUpAdvice? _levelUp;` field, a `PlanLevelUpAsync()` handler, and a card block (after the header, before the identity section) that shows a "Plan level-up" button when `!_editMode && _hero.LatestSnapshot is not null`, and when `_levelUp is not null` renders each candidate's delta as a display-only card (reuse `.sheet-section`/`.card` classes; a small helper `SlotSummary(int[] before, int[] after)` renders the diff). Every option is shown by `Name (Source)` from its `CitedOption`.

- [ ] **Step 2: HeroDetail — hand-off link.** Add, under the card, an anchor:

```razor
<a class="btn btn--ghost"
   href="@($"/?prompt={Uri.EscapeDataString($"Plan my level-up for hero snapshot {_hero!.LatestSnapshot!.Id} ({_hero.Name}).")}")">
   Ask the assistant to recommend →
</a>
```

- [ ] **Step 3: Chat — seed from `?prompt=`.** In `Chat.razor`, inject `NavigationManager`, and in `OnInitializedAsync` parse the `prompt` query param (`Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(Nav.Uri).Query)`) and assign it to the input-bound field if present (seed only — the user presses Send). Confirm the input field name in `Chat.razor` with Serena first.

- [ ] **Step 4: Build + full suite (behavior-neutral)**

Run: `dotnet build` → 0/0. Run: `dotnet test` → green (no behavior change to tested code; the card is UI over the tested service).

- [ ] **Step 5: Live Playwright** (rebuild the app image first — the tightened UI gate)

```bash
docker compose build app && docker compose up -d app   # wait healthy
```
Log in `test`/`test`, open a hero with a snapshot at `/campaigns/{cid}/heroes/{hid}`, click **Plan level-up**: the grounded card renders (HP/PB/slots/features + choices with cited option names). Screenshot desktop (~1280) + mobile (~390); `browser_evaluate` that `document.documentElement.scrollWidth <= innerWidth`. Click "Ask the assistant to recommend →": lands on `/` with the input seeded.

- [ ] **Step 6: Commit**

```bash
git add CompanionUI/Pages/Campaigns/HeroDetail.razor CompanionUI/Pages/Chat.razor
git commit -m "feat(level-up): HeroDetail grounded card + chat recommendation hand-off"
```

---

## Self-Review

**Spec coverage:**
- *Rule-grounded level-up delta* → Tasks 1–4 (`LevelUpPlanner` + slot reader + ref parser), unit-tested; empty slot-change scenario covered.
- *Option menus are real cited entities* → Task 5 (`EntityOptionProvider`, real-Qdrant test) + Task 6 (menus filled per choice); every option is a `CitedOption(id,name,source)`.
- *Recommend with reasons, constrained to the menu* → Task 7 (`plan_level_up` tool + description instructing recommend-from-menu); *unauthenticated → no tool* is the existing `if (TryParse userId)` guard.
- *Advance existing class OR new-class dip* → Task 6 (candidates = existing classes + eligible dips folding in `ResolveMulticlassValidity`); illegal-dip contract asserted.
- *Ownership authorization* → Task 6 (`GetSnapshotForUserAsync` null→throw) + ship-blocker negative test.
- *HeroDetail grounded card + hand-off to chat* → Task 8 (display-only card + `?prompt=` seed) + Playwright.

**Placeholder scan:** the two intentional "confirm the exact shape with Serena" notes (HitDice property names; `EntitySearchQuery` positional args; Chat input field name; QdrantFixture seed API) are grounding checks against real code, not TODO logic — each has a concrete fallback in the shown code. No `dotnet_format`/`.http` change (no endpoint).

**Type consistency:** `CitedOption(Id,Name,Source)`, `LevelUpDelta`, `AdvancementCandidate`, `LevelUpAdvice` are defined once in Task 3 and consumed unchanged in 4/6/7/8. `PlanForUserAsync(heroSnapshotId, userId, targetClass?, considerDip, ct)` signature matches between Task 6 (definition), Task 7 (tool call), and Task 8 (UI call). `SlotsForCasterLevel(SlotSource)` and `ClassFeatureRefParser.Parse(refs, key)` match their Task-1/2 definitions at every call site.

**Global-constraint check:** no new HTTP/MCP surface → no `.http`/`.insomnia`; `AddCharacterAdvice` registered in Program AND the scope-test replica AND pulled in by `AddDndChat`; final verify is the full `dotnet test`; ownership negative test is a ship blocker.
