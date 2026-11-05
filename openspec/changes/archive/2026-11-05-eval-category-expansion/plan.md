# Eval Category Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add monster, rules, lore, npc and not-found cases to the full-chain eval and record per-run detail in the report.

**Architecture:** Extend `Tools/ModelEval/Full` only: a monster branch in `CaseGenerator`, new entries in
`cases/manual.json`, and a per-run detail list in `CaseReport`. Scoring (`Attribution`) is unchanged.

**Tech Stack:** .NET 10 console (`Tools/ModelEval`), System.Text.Json, xunit + FluentAssertions.

**Spec:** `openspec/changes/eval-category-expansion/` (proposal.md, design.md D1–D5, specs/full-chain-eval/spec.md)

## Global Constraints

- Warnings-as-errors (`Directory.Build.props`); `dotnet format` clean on changed files.
- All `.cs` reads/edits via Serena (`mem:workflow/serena_first`); never read `Config/**`.
- Answer variants are never a bare number (unit-qualified only) — matcher digit-boundary rules from `FactMatcher` apply.
- Production code (`Features/**`, `Program.cs`, persona, router) MUST NOT change in this plan.
- Run commands from the repo root; dotnet needs `dangerouslyDisableSandbox` (git-crypt).

## Review Focus

- `fields.ac` shapes in `mm14`: object list `[{"ac":16,"from":[...]}]`, plain int list `[12]`, or int — all three must
  render or the entity is skipped, never throw.
- `fields.cr` may be a string (`"1/2"`) or an object (`{"cr":"5","lair":"6"}`) — object form uses `cr`; unknown → skip.
- A not-found reply that contains a could-not-find phrase AND invents a level still "passes" — accepted limitation
  (design Risks); tests must pin the invented-only reply failing.
- Old reports (no `runDetails`) must deserialize and act as baselines.
- Category filter (`--categories monster`) must select monster cases only.

---

### Task 1: Monster generator + default books

**Files:**
- Modify: `Tools/ModelEval/Full/CaseGenerator.cs`, `Tools/ModelEval/Full/FullModeRunner.cs` (default books), `CLAUDE.md`
  (full-chain paragraph: mention `mm14` and the new categories)
- Test: `DndMcpAICsharpFun.Tests/ModelEval/CaseGeneratorTests.cs`

**Interfaces:**
- Consumes: `EvalCase`, `Fact`, existing `Candidate`/sampling/`CaseSetHash` in `CaseGenerator`.
- Produces: category `"monster"`; ids `gen.monster.<entityId>.ac|hp|cr`; default books `phb14,mm14`.

- [ ] **Step 1: Failing tests** in `CaseGeneratorTests` using a temp canonical dir with a small `mm14.json`:
  - `Monster_ReviewedEntity_ProducesAcHpCrCases` — monster `{ac:[{ac:16}], hp:{average:27}, cr:"1"}` → cases with ids
    ending `.ac`, `.hp`, `.cr`; AC answer variants include `armor class 16` and `ac 16`; none of any case's answer
    variants is a bare number (`variants.Should().NotContain(v => v.All(char.IsDigit))`).
  - `Monster_NeedsReview_Excluded`.
  - `Monster_AcShapes_IntListAndInt_Render` (`ac:[12]` and `ac:12` → `armor class 12`); `Monster_CrObject_UsesCr`
    (`cr:{cr:"5",lair:"6"}` → `challenge rating 5`); unrenderable shapes skipped, no throw.
  - `Monster_ToolDataVariants_IncludeRawJsonForms` (`"ac":16`, `"average":27`, `"cr":"1"`) plus the answer variants.
  - `Monster_CategoryFilter_OnlyMonsters` and `Monster_SameSeed_SameCases`.
- [ ] **Step 2:** run `dotnet test DndMcpAICsharpFun.Tests --filter "FullyQualifiedName~CaseGeneratorTests"` → new tests
  FAIL.
- [ ] **Step 3: Implement** a monster branch mirroring the class/spell pattern (read `type == "Monster"` while iterating
  books; `On("monster")`; sample `CountPerCategory` candidates with the same seeded sampler; one template picked per
  candidate as class/spell do). Prompt forms: "What is the armor class of a {name} in the 2014 Monster Manual?", "How
  many hit points does a {name} have on average in the 2014 Monster Manual?", "What is the challenge rating of a {name}
  in the 2014 Monster Manual?". Answer variants: AC → `armor class {n}`, `armor class of {n}`, `ac {n}`, `ac of {n}`;
  HP → `{n} hit points`, `{n} hp`, `hit points of {n}`, `{n} average hit points`; CR → `challenge rating {cr}`,
  `challenge rating of {cr}`, `cr {cr}`. Expected tools = `ClassSpellTools`. Change the default books in
  `FullModeRunner` to `"phb14,mm14"`.
- [ ] **Step 4:** tests PASS; full `CaseGeneratorTests` + `FullModeRunner` tests green.
- [ ] **Step 5:** `pnpm lint:md` 0 errors; commit `feat(model-eval): generate monster cases from mm14`.

### Task 2: Manual rules / lore / npc / not-found cases

**Files:**
- Modify: `Tools/ModelEval/cases/manual.json`
- Test: `DndMcpAICsharpFun.Tests/ModelEval/ManualCasesTests.cs`, `DndMcpAICsharpFun.Tests/ModelEval/AttributionTests.cs`

**Interfaces:**
- Consumes: manual schema `{id, category, prompt, expectedTools[], toolDataFacts[]|null, answerFacts[]}`.
- Produces: categories `rules`, `lore`, `npc`, `not-found`; ids `manual.<category>.<slug>`.

- [ ] **Step 1: Add cases** (prompts name the book/setting; pick answer words only a correct reply contains):
  - rules ×5, tools `["ask_rules","search_lore","search_dnd"]`, toolDataFacts = answer variants:
    prone → `advantage`; grappled → speed `0`-qualified (`speed becomes 0`, `speed is 0`, `speed of 0`, `speed to 0`);
    opportunity attack → `reach`; Dodge → `disadvantage`; half cover → `+2`.
  - lore ×4 (Eberron), tools `["ask_setting_lore","search_lore","search_dnd"]`, toolDataFacts = answer variants:
    war ended by the Treaty of Thronehold → `last war`; nation destroyed by the Mourning → `cyre`; the City of Towers →
    `sharn`; living constructs built for the Last War → `warforged`.
  - npc ×2, tools `["generate_npc","generate_npc_party"]`: one single-NPC prompt, one "cast/crew" prompt. toolDataFacts
    = the archetype-in-corpus flag as serialized by `/admin/eval/turn` — confirm the exact JSON (property name casing,
    `true` literal) from `NpcGenerationService`'s result types via Serena before writing it; if the party result does
    not carry the flag the same way, use a variant both share. answerFacts = `["hit points","armor class","hp"]`.
  - not-found ×2: a made-up spell ("Zorblax's Shimmering Hexflame") and a made-up monster; tools = the entity+lore set;
    `toolDataFacts: null`; answer variants `couldn't find`, `could not find`, `couldnt find`, `not find`, `no record`,
    `not found`, `don't have`, `do not have`, `no information`.
- [ ] **Step 2: Tests:** `ShippedManualFile_LoadsAllCategories` (loads `Tools/ModelEval/cases/manual.json` via
  `TestPaths`, asserts the four categories present with the counts above and every id unique);
  `NotFoundCase_InventedAnswer_Fails` and `NotFoundCase_CouldNotFindReply_Passes` in `AttributionTests` (synthetic
  `TurnDto` with a `search_lore` call; invented reply "Zorblax's Shimmering Hexflame is a 3rd-level evocation spell" →
  `WrongAnswer`; "I couldn't find that spell in your books" → `Pass`).
- [ ] **Step 3:** tests PASS. Commit `feat(model-eval): add rules, lore, npc and not-found manual cases`.

### Task 3: Per-run detail in the report

**Files:**
- Modify: `Tools/ModelEval/Full/Attribution.cs` (or wherever `RunScore` is produced), `Tools/ModelEval/Full/FullChainRunner.cs`,
  `Tools/ModelEval/Full/EvalReport.cs`
- Test: `DndMcpAICsharpFun.Tests/ModelEval/EvalReportTests.cs`, `FullChainRunnerTests.cs`

**Interfaces:**
- Produces: `RunDetail(IReadOnlyList<string> CalledTools, string Text)` and `CaseReport.RunDetails`
  (`IReadOnlyList<RunDetail>?`, JSON `runDetails`, nullable so old reports load). Text truncated to 600 chars. An
  infra-error run records empty tools and the error text.

- [ ] **Step 1: Failing tests:** `Report_RecordsRunDetail` (runner with a fake client returning a call to `get_entity`
  and a 1000-char text → report case has `RunDetails[0].CalledTools == ["get_entity"]` and `Text.Length == 600`);
  `OldReport_WithoutRunDetails_LoadsAsBaseline` (write a JSON report lacking `runDetails`, `EvalReportStore` loads it,
  `BaselineComparer` compares on shared ids).
- [ ] **Step 2:** implement; keep `Outcomes` unchanged.
- [ ] **Step 3:** tests PASS; full suite green (`dotnet test`); `dotnet build` 0 warnings. Commit
  `feat(model-eval): record called tools and reply text per run`.

### Task 4: Live baseline (controller)

- [ ] Rebuild the app from `main`, run `dotnet run --project Tools/ModelEval -- --mode full --admin-key <key>`, commit
  the report, write `openspec/changes/eval-category-expansion/report.md` (per-category table + findings read from
  `runDetails`), tick `tasks.md`.
