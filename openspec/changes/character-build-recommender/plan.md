# Character Build Recommender Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A concept-to-build recommender (character-coach slice B): from a text concept, return a grounded build-option package (validated class + cited subclass/feat/spell menus) exposed as a per-user `recommend_build` chat tool, so the assistant recommends a single-class build identity constrained to real cited entities.

**Architecture:** Reuse the shipped `Features/CharacterAdvice/` core. Extend `EntityOptionProvider.FeatOptions`/`SpellOptions` with an optional concept query. A new `BuildRecommenderService` validates the class (edition-pinned, mirroring `LevelUpAdviceService`'s lookup), reads the class's structured build info, and assembles the cited menus. A `recommend_build` per-user tool (NOT ownership-gated) exposes it.

**Tech Stack:** .NET 10, `Microsoft.Extensions.AI` chat tools, `IEntityRetrievalService`/`EntitySearchQuery` over `dnd_entities`, System.Text.Json, xUnit + FluentAssertions.

## Global Constraints

- **Serena for ALL `.cs` reads/edits** — built-in Read/Edit/Write on code files is forbidden.
- **Warnings-as-errors** — every build 0 warnings / 0 errors.
- `dotnet` commands run with `dangerouslyDisableSandbox: true` (git-crypt Config); the full suite needs Docker (Testcontainers). Generous timeouts.
- **Grounding contract:** every recommended subclass/feat/spell is a real cited `dnd_entities` record; the class is validated to exist. The tool returns the cited menus; the assistant recommends *from* them.
- **`recommend_build` is NOT ownership-gated** — it takes NO `userId`, touches no owned data; still added in the authenticated tool block (unauthenticated → no tool).
- **Edition-pinned to `"Edition2014"`** (the corpus + rules are 2014, matching `LevelUpAdviceService.LevelUpEdition`).
- **No new HTTP route / MCP-server tool** → NO `DndMcpAICsharpFun.http` / `dnd-mcp-api.insomnia.json` change (in-process chat tool).
- The `FeatOptions`/`SpellOptions` concept param is **optional + defaulted**, so `LevelUpAdviceService`'s existing calls are unchanged (behavior-neutral for slice A).

---

## File Structure

- **Modify** `Features/CharacterAdvice/EntityOptionProvider.cs` — add `string? concept = null` to `FeatOptions` and `SpellOptions`.
- **Create** `Features/CharacterAdvice/BuildRecommendation.cs` — the grounded option-package record.
- **Create** `Features/CharacterAdvice/BuildRecommenderService.cs` — validate class + assemble menus.
- **Modify** `Extensions/ServiceCollectionExtensions.cs` (`AddCharacterAdvice`) — register `BuildRecommenderService`.
- **Modify** `Features/Chat/DndChatService.cs` — ctor dep + `recommend_build` tool.
- **Modify** `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs` — extend the no-`userId` security test + the ctor helper.
- **Test** `DndMcpAICsharpFun.Tests/CharacterAdvice/BuildRecommenderServiceTests.cs`.

---

### Task 1: Concept query on the option provider

**Files:**
- Modify: `Features/CharacterAdvice/EntityOptionProvider.cs`
- Test: `DndMcpAICsharpFun.Tests/CharacterAdvice/EntityOptionProviderIntegrationTests.cs` (extend if a case adds value; otherwise the behavior-neutral full suite is the gate)

**Interfaces:**
- Consumes: `EntitySearchQuery` (`QueryText`, `Type`, `Edition`, `SpellLevel`, `TopK`); `IEntityRetrievalService.SearchAsync`.
- Produces: `FeatOptions(string edition, CancellationToken ct, string? concept = null)` and `SpellOptions(string className, int spellLevel, string edition, CancellationToken ct, string? concept = null)` — when `concept` is non-null it drives `QueryText`; when null, unchanged (`"feat"` / `className`).

**Context:** `FeatOptions` currently hardcodes `QueryText: "feat"`; `SpellOptions` uses `QueryText: className`. For a concept-driven build, the concept should drive relevance (semantic search over effect text). Add the optional param LAST (after `ct`) so existing positional callers are unaffected.

- [ ] **Step 1: Edit `FeatOptions`** — add `, string? concept = null` after `CancellationToken ct`, and change `QueryText: "feat"` → `QueryText: string.IsNullOrWhiteSpace(concept) ? "feat" : concept`.

- [ ] **Step 2: Edit `SpellOptions`** — add `, string? concept = null` after `CancellationToken ct`, and change `QueryText: className` → `QueryText: string.IsNullOrWhiteSpace(concept) ? className : concept`.

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: `0 Warning(s) 0 Error(s)` — `LevelUpAdviceService`'s existing `FeatOptions(edition, ct)` / `SpellOptions(className, level, edition, ct)` calls still compile (the new param defaults).

- [ ] **Step 4: Full suite (behavior-neutral)**

Run: `dotnet test` (Docker up)
Expected: green — the concept param is unused by the level-up caller, so nothing moves.

- [ ] **Step 5: Commit**

```bash
git add Features/CharacterAdvice/EntityOptionProvider.cs
git commit -m "feat(build-recommender): optional concept query on FeatOptions/SpellOptions"
```

---

### Task 2: BuildRecommendation + BuildRecommenderService

**Files:**
- Create: `Features/CharacterAdvice/BuildRecommendation.cs`
- Create: `Features/CharacterAdvice/BuildRecommenderService.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs`
- Test: `DndMcpAICsharpFun.Tests/CharacterAdvice/BuildRecommenderServiceTests.cs`

**Interfaces:**
- Consumes: `IEntityRetrievalService.SearchDiagnosticAsync(EntitySearchQuery, ct) → IList<EntityDiagnosticResult>` (`.Name`, `.Edition`, `.SourceBook`, `.Fields` JsonElement); `EntityType.Class`; `ClassFields` (`Domain.Entities.Fields` — `Hd: HitDice?`, `Proficiency: IReadOnlyList<string>?`, `SubclassTitle: string?`); `HitDice(int Number, int Faces)`; `EntityOptionProvider` (`SubclassOptions(className, edition, ct)`, `FeatOptions(edition, ct, concept)`, `SpellOptions(className, spellLevel, edition, ct, concept)` from Task 1); `CitedOption(string Id, string Name, string Source)`.
- Produces: `BuildRecommendation` (record) and `BuildRecommenderService.RecommendBuildOptionsAsync(string className, string concept, int? targetLevel, CancellationToken ct) → Task<BuildRecommendation>`.

**Context:** Mirror `LevelUpAdviceService`'s edition-pinned class lookup: `SearchDiagnosticAsync(QueryText: className, Type: EntityType.Class, Edition: BuildEdition, …, TopK: 5)` then `FirstOrDefault(r => Name==className && Edition==BuildEdition)`. `BuildEdition = "Edition2014"`. `ClassFields` does NOT carry `spellcastingAbility`, so read it from the raw `Fields` JsonElement (`fields.TryGetProperty("spellcastingAbility", …)`). Casters (spellcasting ability present) get level-1 concept spells; non-casters get an empty spell menu. When the class isn't found, return `ClassInCorpus=false` + the available class names (a Type=Class search, distinct names).

- [ ] **Step 1: Create `BuildRecommendation`**

```csharp
// Features/CharacterAdvice/BuildRecommendation.cs
namespace DndMcpAICsharpFun.Features.CharacterAdvice;

/// <summary>Grounded build-option package for a concept + chosen class — the assistant composes the
/// actual build FROM these cited menus (never invents). Not the final recommendation.</summary>
public sealed record BuildRecommendation(
    bool ClassInCorpus,
    string ClassName,
    string? HitDie,                 // e.g. "d10", null if unknown
    string? SpellcastingAbility,    // e.g. "int"/"wis"/"cha", null for non-casters
    IReadOnlyList<string> SaveProficiencies,
    string? SubclassTitle,          // e.g. "Martial Archetype"
    IReadOnlyList<CitedOption> Subclasses,
    IReadOnlyList<CitedOption> Feats,
    IReadOnlyList<CitedOption> Spells,
    IReadOnlyList<string> AvailableClasses)   // populated when ClassInCorpus is false
{
    public static BuildRecommendation NotInCorpus(string className, IReadOnlyList<string> available) =>
        new(false, className, null, null, [], null, [], [], [], available);
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
// DndMcpAICsharpFun.Tests/CharacterAdvice/BuildRecommenderServiceTests.cs
// Uses a fake IEntityRetrievalService (substitute / hand-rolled) — read how BuildRecommender's peers
// (EntityOptionProvider tests / LevelUpAdviceServiceTests fakes) build a fake IEntityRetrievalService, and reuse it.
// Fake behavior:
//  - SearchDiagnosticAsync(Type=Class, QueryText="Fighter", Edition="Edition2014") → one result Name="Fighter",
//    Edition="Edition2014", Fields = { "hd":{"number":1,"faces":10}, "proficiency":["str","con"],
//    "subclassTitle":"Martial Archetype" }  (no spellcastingAbility → non-caster)
//  - SearchDiagnosticAsync(Type=Subclass, QueryText="Fighter", …) → a Fighter subclass (SubclassFields.className="Fighter")
//  - SearchAsync(Type=Feat, …) → a couple feats
//  - SearchAsync(Type=Class, …) [available-classes lookup] → Names ["Fighter","Wizard"]
//
// [Fact] valid class → structured info + cited menus
//   var rec = await svc.RecommendBuildOptionsAsync("Fighter", "a tanky front-liner", null, default);
//   rec.ClassInCorpus.Should().BeTrue();
//   rec.HitDie.Should().Be("d10");
//   rec.SubclassTitle.Should().Be("Martial Archetype");
//   rec.SaveProficiencies.Should().Contain("str");
//   rec.Subclasses.Should().NotBeEmpty();  rec.Feats.Should().NotBeEmpty();
//   rec.Spells.Should().BeEmpty();   // non-caster
//
// [Fact] class not in corpus → not-found + available list
//   var rec = await svc.RecommendBuildOptionsAsync("Artificer", "a gadgeteer", null, default);
//   rec.ClassInCorpus.Should().BeFalse();
//   rec.AvailableClasses.Should().Contain("Fighter");
//
// [Fact] concept reaches feat/spell retrieval (caster)
//   // fake Fighter fields include "spellcastingAbility":"int" for this test's class → svc calls SpellOptions with concept;
//   // assert the fake captured QueryText == the concept for the Type=Spell search (i.e. concept-relevant retrieval).
```

> Fill these in against the real fake-retrieval pattern (Serena: read `EntityOptionProviderIntegrationTests`/`LevelUpAdviceServiceTests` for how they stand up `IEntityRetrievalService`). If a hand-rolled fake is simplest, capture the last `EntitySearchQuery` per `Type` so the concept-in-query assertion is real.

- [ ] **Step 3: Run to verify they fail** — `dotnet test --filter "FullyQualifiedName~BuildRecommenderServiceTests"` → FAIL (type missing).

- [ ] **Step 4: Implement `BuildRecommenderService`**

```csharp
// Features/CharacterAdvice/BuildRecommenderService.cs
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Retrieval.Entities;

namespace DndMcpAICsharpFun.Features.CharacterAdvice;

public sealed class BuildRecommenderService(IEntityRetrievalService retrieval, EntityOptionProvider options)
{
    private const string BuildEdition = "Edition2014";
    private const int TopK = 20;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<BuildRecommendation> RecommendBuildOptionsAsync(
        string className, string concept, int? targetLevel, CancellationToken ct)
    {
        var classResults = await retrieval.SearchDiagnosticAsync(
            new EntitySearchQuery(className, EntityType.Class, null, BuildEdition, null, null, null,
                null, null, null, null, TopK), ct);
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
                    new EntitySearchQuery("class", EntityType.Class, null, BuildEdition, null, null, null,
                        null, null, null, null, TopK), ct);
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
            && sa.ValueKind == JsonValueKind.String ? sa.GetString() : null;

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
```

> Confirm `ClassFields`'s real property names (`Hd`/`Proficiency`/`SubclassTitle`) and `HitDice.Faces`, and `EntityDiagnosticResult.Fields`/`.Name`/`.Edition`, with Serena before finalizing; adjust if they differ.

- [ ] **Step 5: Run tests → PASS.**

- [ ] **Step 6: Register in DI** — in `Extensions/ServiceCollectionExtensions.cs` `AddCharacterAdvice`, add `services.AddScoped<BuildRecommenderService>();` (match the existing `EntityOptionProvider`/`LevelUpAdviceService` lifetime — read the method with Serena).

- [ ] **Step 7: Build 0/0; full `dotnet test` green. Commit**

```bash
git add Features/CharacterAdvice/BuildRecommendation.cs Features/CharacterAdvice/BuildRecommenderService.cs Extensions/ServiceCollectionExtensions.cs DndMcpAICsharpFun.Tests/CharacterAdvice/BuildRecommenderServiceTests.cs
git commit -m "feat(build-recommender): BuildRecommenderService (validate class + cited menus)"
```

---

### Task 3: `recommend_build` chat tool + security regression

**Files:**
- Modify: `Features/Chat/DndChatService.cs`
- Modify: `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs`

**Interfaces:**
- Consumes: `BuildRecommenderService.RecommendBuildOptionsAsync` (Task 2); the per-user-tool pattern in `SendAsync` (`AIFunctionFactory.Create`, the authenticated `if (long.TryParse(idClaim, out var userId))` block).
- Produces: a `recommend_build(className, concept, targetLevel?)` tool with NO `userId` argument.

**Context:** `recommend_build` is not ownership-gated, so its lambda takes NO `userId` (unlike `plan_level_up`). It's still registered inside the authenticated block (unauthenticated → no tool). Add `BuildRecommenderService buildRecommender` to `DndChatService`'s primary ctor. The existing `DndChatServiceTests` helper `new()`s `DndChatService` → add a `BuildRecommenderService` to its ctor helper (mirror `BuildLevelUpAdviceService`).

- [ ] **Step 1: Add the ctor dependency** — append `BuildRecommenderService buildRecommender` to the `DndChatService` primary constructor (after `LevelUpAdviceService levelUpService`).

- [ ] **Step 2: Register the tool** inside the `if (long.TryParse(idClaim, out var userId))` block in `SendAsync`, alongside the others (note: NO `userId` in the lambda):

```csharp
            toolList.Add(AIFunctionFactory.Create(
                (string className, string concept, int? targetLevel, CancellationToken toolCt) =>
                    buildRecommender.RecommendBuildOptionsAsync(className, concept, targetLevel, toolCt),
                name: "recommend_build",
                description: "Recommend a single-class D&D character build for a text concept (e.g. 'a tanky " +
                    "dwarf who controls the battlefield'). YOU pick the class that best fits the concept and " +
                    "pass it as className plus the concept; if the result says the class is not in the corpus, " +
                    "pick a different class from its availableClasses list and call again. Then recommend a " +
                    "subclass, key feats, and signature spells STRICTLY from the returned cited options, plus " +
                    "ability-score priorities from the returned save proficiencies / spellcasting ability, and " +
                    "explain why it fits the concept. Never invent a subclass, feat, or spell. Single-class only " +
                    "— if the concept implies multiclassing, recommend the primary class and note a dip direction."));
```

- [ ] **Step 3: Fix the test ctor helper** — in `DndChatServiceTests.cs`, add a `BuildRecommenderService` to the `CreateService`/`BuildLevelUpAdviceService`-style helper so `new DndChatService(...)` compiles (mirror how the level-up dep was threaded).

- [ ] **Step 4: Extend the security-regression test** — the test `Encounter_tool_schemas_do_not_expose_userId_as_a_caller_supplied_argument` filters tool names to `"rate_encounter"`/`"build_encounter"`/`"plan_level_up"`; add `"recommend_build"` to that filter so it's covered (its lambda has no `userId`, so it passes — this guards against a future regression).

- [ ] **Step 5: Build 0/0; full `dotnet test` green. Commit**

```bash
git add Features/Chat/DndChatService.cs DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs
git commit -m "feat(build-recommender): recommend_build chat tool (not ownership-gated)"
```

---

## Self-Review

**Spec coverage:**
- "Concept-to-build option package grounded in cited entities" → Task 2 (`BuildRecommenderService` assembles structured info + cited menus) + Task 3 (tool description constrains the recommendation to the menus).
- "Class validated; unknown class → available set" → Task 2 (`ClassInCorpus`/`AvailableClasses`, the not-found path) + test.
- "Feats/spells retrieved by the concept" → Task 1 (concept query) + Task 2 (passes `concept` to `FeatOptions`/`SpellOptions`) + the concept-in-query test.
- "Single-class; multiclass defers to level-up assistant" → Task 3 (tool description: single-class, note a dip).
- "Per-user chat tool, not ownership-gated, no userId, auth-only" → Task 3 (tool in the auth block, no `userId` lambda) + the extended security-regression test.

**Placeholder scan:** the "confirm with Serena" notes (the fake-retrieval pattern; `ClassFields`/`HitDice`/`EntityDiagnosticResult` shapes; the DI lifetime; the ctor-helper thread) are grounding checks against real code with concrete fallbacks — not TODO logic.

**Type consistency:** `BuildRecommendation` (Task 2) is consumed by the tool in Task 3; `RecommendBuildOptionsAsync(string, string, int?, CancellationToken)` matches between Task 2 (def) and Task 3 (call). `FeatOptions(edition, ct, concept?)` / `SpellOptions(className, spellLevel, edition, ct, concept?)` (Task 1) are called with the trailing `concept` in Task 2. `CitedOption(Id, Name, Source)` reused unchanged. `BuildEdition = "Edition2014"` matches the level-up edition pin.
