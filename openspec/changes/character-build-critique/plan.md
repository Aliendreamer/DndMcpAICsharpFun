# Character Build Critique Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An ownership-gated build-critique (character-coach slice C, the last): compute deterministic grounded findings for an owned hero (untaken choices, stat consistency, ability alignment) that the assistant frames, exposed as a `critique_build` chat tool + a HeroDetail card.

**Architecture:** Reuse the slice-A/B core. `BuildCritiqueService` resolves the owned snapshot (or throws) and emits `CritiqueFinding`s: (A) untaken choices from the class entity's per-level `classFeatures` (edition-pinned lookup + `ClassFeatureRefParser`, name-matched vs the sheet); (B) stat consistency from shipped primitives (`MulticlassSpellcasting.SpellcastingAbility`, `CharacterSheet.Modifier`/`ProficiencyBonus`, the PHB slot table); (C) ability alignment (highest ability vs the class's spellcasting ability). A `critique_build` per-user tool + a HeroDetail card mirror slice A.

**Tech Stack:** .NET 10, `Microsoft.Extensions.AI` chat tools, Blazor Server, `IEntityRetrievalService` over `dnd_entities`, System.Text.Json, xUnit + FluentAssertions.

## Global Constraints

- **Serena for ALL `.cs`/`.razor` reads/edits** — built-in Read/Edit/Write on code files is forbidden (Razor edits use `replace_content`/`create_text_file`).
- **Warnings-as-errors** — every build 0 warnings / 0 errors.
- `dotnet` commands run with `dangerouslyDisableSandbox: true` (git-crypt Config); the full suite needs Docker. Generous timeouts.
- **Grounding contract:** every finding is anchored to a concrete fact about the *actual* sheet; the untaken-feature finding cites the real class rule; the assistant frames, never free-judges.
- **Ownership:** `BuildCritiqueService.CritiqueForUserAsync` resolves via `HeroRepository.GetSnapshotForUserAsync(snapshotId, userId)` and throws `UnauthorizedAccessException("Hero snapshot not found or not owned by the caller.")` on null (verbatim, like `LevelUpAdviceService`). `critique_build` closes over the session `userId` (no `userId` arg).
- **Edition-pinned to `"Edition2014"`** for the class-entity lookup (matching `LevelUpAdviceService`).
- **A new per-user tool joins BOTH guard tests** — the `DndChatServiceTests` no-`userId`-arg filter AND the auth-present / unauth-absent presence tests (dev-flow gate).
- **No new HTTP route / MCP-server tool** → NO `.http`/`.insomnia` change.

---

## File Structure

- **Create** `Features/CharacterAdvice/BuildCritique.cs` — `CritiqueFinding` + `BuildCritique` records.
- **Create** `Features/CharacterAdvice/BuildCritiqueService.cs` — ownership-gated; the three finding computations.
- **Modify** `Features/CharacterAdvice/CharacterAdviceServiceCollectionExtensions.cs` — register the service.
- **Modify** `Features/Chat/DndChatService.cs` — ctor dep + `critique_build` tool.
- **Modify** `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs` — the no-`userId` filter + presence lists + ctor helper.
- **Modify** `CompanionUI/Pages/Campaigns/HeroDetail.razor` — "Review this build" card.
- **Test** `DndMcpAICsharpFun.Tests/CharacterAdvice/BuildCritiqueServiceTests.cs`.

---

### Task 1: BuildCritique + BuildCritiqueService

**Files:**
- Create: `Features/CharacterAdvice/BuildCritique.cs`
- Create: `Features/CharacterAdvice/BuildCritiqueService.cs`
- Modify: `Features/CharacterAdvice/CharacterAdviceServiceCollectionExtensions.cs`
- Test: `DndMcpAICsharpFun.Tests/CharacterAdvice/BuildCritiqueServiceTests.cs`

**Interfaces:**
- Consumes: `HeroRepository.GetSnapshotForUserAsync(long, long) → HeroSnapshot?` (`.Sheet`); `CharacterSheet` (`Classes` (`ClassLevel` Class/Subclass/Level), `Strength`..`Charisma`, `SpellcastingAbility` string, `SpellSaveDC`/`SpellAttackBonus` int, `SpellSlots` int[9], `Features` (`CharacterFeature.Name`), `ProficiencyBonus`, `Modifier(int)`, `ProficiencyBonusForLevel(int)`); `IEntityRetrievalService.SearchDiagnosticAsync` (class entity, edition-pinned, like `LevelUpAdviceService`); `ClassFields` (`ClassFeatures`, `SubclassTitle`); `ClassFeatureRefParser.Parse(refs, "classFeature") → IReadOnlyList<FeatureRef>` (`Name`/`Source`/`Level`); `EntityNameIndex.Normalize(string)`; `MulticlassSpellcasting.SpellcastingAbility(className) → string?` and `.ResolveSlotSource(IEnumerable<ClassLevel>) → SlotSource`; `MulticlassSlotTableSeeder.SlotsForCasterLevel(SlotSource) → int[9]`.
- Produces: `CritiqueFinding`/`BuildCritique` records + `BuildCritiqueService.CritiqueForUserAsync(long heroSnapshotId, long userId, CancellationToken ct) → Task<BuildCritique>`.

**Context:** The three finding sets. **(A) Untaken choices** needs the class entity (for `classFeatures` + `subclassTitle`), so mirror `LevelUpAdviceService`'s edition-pinned class lookup and parse with `ClassFeatureRefParser`. Subclass-selection level = the earliest level a `classFeatures` entry's name contains the class's `SubclassTitle` (same signal `LevelUpPlanner` uses for `IsSubclassSelectionLevel`); if `classLevel >= that level` and `Subclass` is empty → finding. Missing features = the parsed feature names up to `classLevel`, `EntityNameIndex.Normalize`-matched against `sheet.Features` names, minus present. **(B) + (C)** are pure-sheet + shipped primitives — no class-entity lookup: the spellcasting ability comes from `MulticlassSpellcasting.SpellcastingAbility(className)`; DC = `8 + PB + castMod`, attack = `PB + castMod`, slots = `SlotsForCasterLevel(ResolveSlotSource(sheet.Classes))`.

- [ ] **Step 1: Create `BuildCritique`**

```csharp
// Features/CharacterAdvice/BuildCritique.cs
namespace DndMcpAICsharpFun.Features.CharacterAdvice;

public enum CritiqueKind { UntakenChoice, StatConsistency, AbilityAlignment }

/// <summary>One grounded observation about the build, anchored to a concrete sheet fact and, where
/// relevant, a real cited rule (Cite). The assistant frames these into a critique — it does not judge.</summary>
public sealed record CritiqueFinding(CritiqueKind Kind, string Observation, CitedOption? Cite);

public sealed record BuildCritique(
    long HeroSnapshotId,
    IReadOnlyList<CritiqueFinding> Findings,
    IReadOnlyList<string> Strengths);
```

- [ ] **Step 2: Write the failing tests**

```csharp
// DndMcpAICsharpFun.Tests/CharacterAdvice/BuildCritiqueServiceTests.cs
// Uses PostgresFixture to seed two users each owning a hero+snapshot (mirror LevelUpAdviceServiceTests),
// and a fake IEntityRetrievalService returning a Fighter ClassFields (hd, classFeatures incl.
// "Martial Archetype|Fighter|PHB|3" and "Extra Attack|Fighter|PHB|5", subclassTitle "Martial Archetype").
// Read LevelUpAdviceServiceTests with Serena for the exact seeding + fake pattern.

[Fact] // ownership — SHIP BLOCKER
public async Task Critique_otherUsersSnapshot_throws()
{
    await FluentActions.Awaiting(() => service.CritiqueForUserAsync(snapshotId, otherUserId, default))
        .Should().ThrowAsync<UnauthorizedAccessException>();
}

[Fact] // (A) missing feature
public async Task Level5Fighter_missingExtraAttack_isFlagged()
{
    // snapshot sheet: Fighter level 5, Subclass "Champion", Features WITHOUT "Extra Attack".
    var c = await service.CritiqueForUserAsync(snapshotId, ownerUserId, default);
    c.Findings.Should().Contain(f => f.Kind == CritiqueKind.UntakenChoice
        && f.Observation.Contains("Extra Attack"));
}

[Fact] // (A) subclass not chosen
public async Task Fighter3_noSubclass_isFlagged()
{
    // sheet: Fighter level 3, Subclass "" → past the level-3 Martial Archetype choice.
    var c = await service.CritiqueForUserAsync(snapshotId, ownerUserId, default);
    c.Findings.Should().Contain(f => f.Kind == CritiqueKind.UntakenChoice
        && f.Observation.Contains("subclass", StringComparison.OrdinalIgnoreCase));
}

[Fact] // (A) formatting variant NOT a false positive
public async Task FeatureNameFormattingVariant_isNotMissing()
{
    // sheet Features has "Extra Attack (1)"; Normalize("Extra Attack (1)")==Normalize("Extra Attack") → present.
    var c = await service.CritiqueForUserAsync(snapshotId, ownerUserId, default);
    c.Findings.Should().NotContain(f => f.Kind == CritiqueKind.UntakenChoice
        && f.Observation.Contains("Extra Attack"));
}

[Fact] // (B) stat mismatch
public async Task RecordedSaveDcDiffersFromComputed_isFlagged()
{
    // caster sheet: SpellcastingAbility "Wisdom", WIS 16 (+3), level→PB +2 → computed DC 8+2+3=13; recorded 11.
    var c = await service.CritiqueForUserAsync(snapshotId, ownerUserId, default);
    c.Findings.Should().Contain(f => f.Kind == CritiqueKind.StatConsistency);
}

[Fact] // (C) ability misalignment
public async Task CasterHighestAbilityNotCastingAbility_isFlagged()
{
    // Wizard sheet with STR 16 > INT 12 → alignment finding.
    var c = await service.CritiqueForUserAsync(snapshotId, ownerUserId, default);
    c.Findings.Should().Contain(f => f.Kind == CritiqueKind.AbilityAlignment);
}

[Fact] // clean build → no findings
public async Task CleanBuild_hasNoFindings() { /* a consistent, fully-recorded Fighter → Findings empty */ }
```

> Fill these against the real `LevelUpAdviceServiceTests` seeding helpers (Serena). Seed each scenario's sheet via the snapshot's `CharacterJson`.

- [ ] **Step 3: Run to verify they fail** — `dotnet test --filter "FullyQualifiedName~BuildCritiqueServiceTests"` → FAIL (type missing). Docker up.

- [ ] **Step 4: Implement `BuildCritiqueService`**

```csharp
// Features/CharacterAdvice/BuildCritiqueService.cs
using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction; // EntityNameIndex
using DndMcpAICsharpFun.Features.Resolution;                  // MulticlassSpellcasting, MulticlassSlotTableSeeder
using DndMcpAICsharpFun.Features.Retrieval.Entities;

namespace DndMcpAICsharpFun.Features.CharacterAdvice;

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
                    $"Your {cls.Class} is level {cls.Level} but has no {title} chosen (due at level {sl}).",
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
                    $"Your {cls.Class}'s spellcasting ability is {castAbility} ({AbilityScore(sheet, castAbility)}), but your highest score is {highName} ({highScore}).", null));
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
```

> Confirm with Serena: `ClassFields.ClassFeatures`/`SubclassTitle`, `ClassFeatureRefParser.Parse` signature, `EntityNameIndex` namespace, `MulticlassSpellcasting.ResolveSlotSource`/`MulticlassSlotTableSeeder.SlotsForCasterLevel`, `CharacterSheet.ProficiencyBonus`/`Modifier`/`ModifierStr`, `HeroRepository.GetSnapshotForUserAsync`, `CitedOption`. Adjust the `EntitySearchQuery` positional args to the real record if needed (it's the same shape the level-up service uses).

- [ ] **Step 5: Run tests → PASS.**

- [ ] **Step 6: Register in DI** — add `services.AddScoped<BuildCritiqueService>();` to `AddCharacterAdvice` in `Features/CharacterAdvice/CharacterAdviceServiceCollectionExtensions.cs` (match the peers' lifetime — read it with Serena).

- [ ] **Step 7: Build 0/0; full `dotnet test` green. Commit**

```bash
git add Features/CharacterAdvice/BuildCritique.cs Features/CharacterAdvice/BuildCritiqueService.cs Features/CharacterAdvice/CharacterAdviceServiceCollectionExtensions.cs DndMcpAICsharpFun.Tests/CharacterAdvice/BuildCritiqueServiceTests.cs
git commit -m "feat(build-critique): ownership-gated BuildCritiqueService (untaken/stat/alignment findings)"
```

---

### Task 2: `critique_build` chat tool + security/presence tests

**Files:**
- Modify: `Features/Chat/DndChatService.cs`
- Modify: `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs`

**Interfaces:**
- Consumes: `BuildCritiqueService.CritiqueForUserAsync` (Task 1); the ownership-gated per-user-tool pattern (`plan_level_up` — closes over `userId`).
- Produces: a `critique_build(heroSnapshotId)` tool.

**Context:** `critique_build` IS ownership-gated (unlike `recommend_build`), so it closes over `userId` and takes no `userId` arg — same shape as `plan_level_up`. Add the ctor dep + the tool inside the authenticated block. Then the dev-flow both-guard-tests gate: add `critique_build` to the no-`userId`-arg filter AND the auth-present/unauth-absent presence tests, and thread a `BuildCritiqueService` through the test ctor helper.

- [ ] **Step 1: Add the ctor dep** — append `BuildCritiqueService critiqueService` to `DndChatService`'s primary constructor (after `BuildRecommenderService buildRecommender`).

- [ ] **Step 2: Register the tool** inside the `if (long.TryParse(idClaim, out var userId))` block:

```csharp
            toolList.Add(AIFunctionFactory.Create(
                (long heroSnapshotId, CancellationToken toolCt) =>
                    critiqueService.CritiqueForUserAsync(heroSnapshotId, userId, toolCt),
                name: "critique_build",
                description: "Review a hero snapshot the signed-in user owns and critique the build. Returns " +
                    "grounded findings — untaken choices (a subclass not chosen, class features not recorded), " +
                    "stat inconsistencies (recorded save DC/attack/slots vs computed), and ability misalignment. " +
                    "Frame these into a critique anchored to the findings; do NOT invent problems or free-judge. " +
                    "Where a finding suggests it, hand off: an untaken subclass/feature → suggest plan_level_up; " +
                    "an ability misalignment → suggest recommend_build."));
```

- [ ] **Step 3: Thread the test ctor helper** — in `DndChatServiceTests.cs`, add a `BuildCritiqueService` to the `CreateService` helper (mirror how `BuildRecommenderService`/`BuildLevelUpAdviceService` are threaded — a `BuildBuildCritiqueService()` over `NoOpDbFactory`-backed `HeroRepository` + a substitutable `IEntityRetrievalService`). Minimum to compile; no assertion change.

- [ ] **Step 4: Extend BOTH guard tests** — (a) add `"critique_build"` to the `...userId_as_a_caller_supplied_argument` filter (it's ownership-gated → closes over `userId`, its schema has no `userId` arg → passes); (b) add `"critique_build"` to the authenticated-tools-present list and the unauthenticated-tools-absent list.

- [ ] **Step 5: Build 0/0; full `dotnet test` green. Commit**

```bash
git add Features/Chat/DndChatService.cs DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs
git commit -m "feat(build-critique): critique_build chat tool + both guard tests"
```

---

### Task 3: HeroDetail "Review this build" card

**Files:**
- Modify: `CompanionUI/Pages/Campaigns/HeroDetail.razor`

**Interfaces:**
- Consumes: `BuildCritiqueService.CritiqueForUserAsync` (Task 1); `_hero.LatestSnapshot.Id`, the `_userId` field (from the level-up card task).
- Produces: a display-only findings card + a `?prompt=` hand-off.

**Context:** Mirror the shipped "Plan level-up" card (`@inject`, the `_userId` field, a handler, a display-only card, an "Ask the assistant → " `?prompt=` link). Read the level-up card block in `HeroDetail.razor` with Serena and follow it exactly.

- [ ] **Step 1: Inject + handler + card** — add `@inject DndMcpAICsharpFun.Features.CharacterAdvice.BuildCritiqueService CritiqueSvc`, a `BuildCritique? _critique` field, a `ReviewBuildAsync()` handler (`_critique = await CritiqueSvc.CritiqueForUserAsync(_hero!.LatestSnapshot!.Id, _userId, default);` in a try/catch that stores an error string), and a card section (shown when `!_editMode && _hero.LatestSnapshot is not null`, parallel to the level-up card): a "Review this build" button; when `_critique is not null`, render the findings grouped by `Kind` (each `Observation`, and where `Cite is not null` show `Name (Source)`), or "No issues found — your build looks consistent." when `Findings` is empty.

- [ ] **Step 2: Hand-off link** — under the card, an `<a class="btn btn--ghost" href="@($"/?prompt={Uri.EscapeDataString($"Critique my build for hero snapshot {_hero!.LatestSnapshot!.Id} ({_hero.Name}).")}")">Ask the assistant to critique →</a>`.

- [ ] **Step 3: Build 0/0; full suite green** (behavior-neutral for the rest of HeroDetail).

- [ ] **Step 4: Live Playwright** (rebuild the app image first — `docker compose build app && docker compose up -d app`): seed a hero with a gap (a level-5 Fighter missing "Extra Attack"), click "Review this build" → the findings card renders the untaken-feature finding; screenshot desktop (~1280) + mobile (~390); `browser_evaluate` no horizontal overflow; the critique action routes to `/` with the prompt seeded.

- [ ] **Step 5: Commit**

```bash
git add CompanionUI/Pages/Campaigns/HeroDetail.razor
git commit -m "feat(build-critique): HeroDetail 'Review this build' card + chat hand-off"
```

---

## Self-Review

**Spec coverage:**
- "Ownership-gated findings; clean build → no findings; never free-judge" → Task 1 (ownership throw + empty-findings on a clean build; the tool description forbids free-judging) + tests.
- "Untaken-choices findings (subclass + missing features, name-normalized)" → Task 1 (A) + the missing-feature / subclass / formatting-variant tests.
- "Stat-consistency findings" → Task 1 (B) + the save-DC-mismatch test.
- "Ability-alignment findings" → Task 1 (C) + the misalignment test.
- "Per-user chat tool + HeroDetail card, ownership-gated, no userId arg, auth-only" → Task 2 (tool in auth block, closes over userId, both guard tests) + Task 3 (card + hand-off).

**Placeholder scan:** the "confirm with Serena" notes (the `LevelUpAdviceServiceTests` seeding pattern; exact `ClassFields`/`ClassFeatureRefParser`/`EntityNameIndex`/slot-primitive/`CharacterSheet` signatures; the level-up card block to mirror) are grounding checks against real code with concrete fallbacks — not TODO logic.

**Type consistency:** `CritiqueFinding(CritiqueKind, string, CitedOption?)` and `BuildCritique(long, IReadOnlyList<CritiqueFinding>, IReadOnlyList<string>)` (Task 1) are consumed unchanged in Tasks 2–3; `CritiqueForUserAsync(long, long, CancellationToken)` matches between Task 1 (def), Task 2 (tool call), Task 3 (card call). `BuildEdition = "Edition2014"` matches the level-up pin. `CitedOption(Id, Name, Source)` reused unchanged.
