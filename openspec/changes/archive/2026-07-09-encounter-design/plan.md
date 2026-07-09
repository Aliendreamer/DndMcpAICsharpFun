# Encounter Design Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A companion "encounter design" surface — rate a given encounter's difficulty and build one for a target difficulty — with one deterministic math core (both editions) shared by rate and build, exposed as two per-user chat tools.

**Architecture:** Pure `EncounterMath` (CR→XP + per-edition party tables + 2014 multiplier) → `EncounterAssessor` (rate) → `EncounterGenerator` (build; retrieves monsters via the existing entity search and returns the Assessor's verdict, so build↔rate never disagree) → `EncounterDesignService` (party from the caller's campaign heroes, ownership-checked, or explicit `partyLevels`) → two per-user `AIFunctionFactory` chat tools in `DndChatService`.

**Tech Stack:** .NET 10, xUnit + FluentAssertions, Microsoft.Extensions.AI `AIFunctionFactory`, existing `IEntityRetrievalService` / `EntityFilters`, `HeroRepository` / `CampaignRepository`.

## Global Constraints

- `net10.0`; nullable; **warnings-as-errors**. Central Package Management (version-less `PackageReference`).
- Use **Serena** for all `.cs` reads/edits; every subagent prompt includes the CRITICAL-Serena block + `initial_instructions`. Run `dotnet` with `dangerouslyDisableSandbox: true`. Solution is `.slnx`.
- `EncounterMath` and the classifier are PURE (no I/O) and table-driven — table-tested.
- **Build and rate share ONE difficulty definition:** the Generator returns the `EncounterAssessor`'s verdict over its chosen monsters; it never re-implements classification.
- Per-user tools follow the SEC-08 pattern: added inside `DndChatService`'s `long.TryParse(idClaim, out userId)` block, closing over `userId`, routing through `*ForUserAsync` with an ownership check; never accept a user/campaign id as a trusted argument on the shared-key MCP surface. Ship a negative-ownership test.
- No new HTTP route → `.http`/`.insomnia` unchanged. `ingest-entities` finish-step is not applicable (no canonical/extraction change).
- **DMG tables are correctness-critical:** transcribe the FULL tables from a cited reference (2014 DMG p.82 thresholds + encounter multipliers; 2024 DMG XP-budget table; standard CR→XP table) and spot-check the values below in tests. If a transcribed value disagrees with the spot-check, the reference wins — fix the spot-check comment, never fudge the table.

---

### Task 1: EncounterMath core (pure, both editions)

**Files:**
- Create: `Features/Encounters/EncounterMath.cs`
- Create: `Features/Encounters/Edition.cs` (or reuse `Domain.DndVersion` — prefer reusing `DndVersion { Edition2014, Edition2024 }`)
- Create: `Features/Encounters/Difficulty.cs` — `enum Difficulty { Trivial, Easy, Medium, Hard, Deadly }` (2024 maps Low→Easy, Moderate→Medium, High→Hard; below-Easy = Trivial)
- Test: `DndMcpAICsharpFun.Tests/Encounters/EncounterMathTests.cs`

**Interfaces / Produces:**
- `static class EncounterMath`:
  - `int CrToXp(double cr)` — standard table (CR 0→10, ⅛→25, ¼→50, ½→100, 1→200, 2→450, 3→700, 5→1800, 10→5900, 20→25000, 30→155000; transcribe all rows 0..30).
  - `int[] PartyBudget(IReadOnlyList<int> levels, DndVersion ed)` — 2014 → 4 summed thresholds [Easy,Med,Hard,Deadly]; 2024 → 3 summed budgets [Low,Mod,High].
  - `double Multiplier(int monsterCount, int partySize, DndVersion ed)` — 2014 only (2024 returns 1.0). Base by count: 1→1, 2→1.5, 3–6→2, 7–10→2.5, 11–14→3, 15+→4; shift one step up when partySize<3, one step down when partySize≥6.
  - `Difficulty Classify(int totalMonsterXp, IReadOnlyList<int> levels, int monsterCount, DndVersion ed)` — computes budget + (2014) multiplier, classifies `xp×mult` (2014) or raw `xp` (2024) into a band.

- [ ] **Step 1: Write the failing table tests**

```csharp
using DndMcpAICsharpFun.Domain;              // DndVersion
using DndMcpAICsharpFun.Features.Encounters;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Encounters;

public sealed class EncounterMathTests
{
    [Theory]
    [InlineData(0, 10)] [InlineData(0.25, 50)] [InlineData(1, 200)]
    [InlineData(5, 1800)] [InlineData(10, 5900)] [InlineData(20, 25000)] [InlineData(30, 155000)]
    public void CrToXp_matches_standard_table(double cr, int xp) =>
        EncounterMath.CrToXp(cr).Should().Be(xp);

    [Fact]
    public void Party_thresholds_2014_sum_over_party()
    {
        // 4× level-5 PCs: per-char Easy/Med/Hard/Deadly = 250/500/750/1100  (DMG p.82)
        var b = EncounterMath.PartyBudget(new[] { 5, 5, 5, 5 }, DndVersion.Edition2014);
        b.Should().Equal(1000, 2000, 3000, 4400);
    }

    [Fact]
    public void Party_budget_2024_sum_over_party()
    {
        // 4× level-5 PCs: per-char Low/Mod/High = 500/750/1100 (2024 DMG)
        var b = EncounterMath.PartyBudget(new[] { 5, 5, 5, 5 }, DndVersion.Edition2024);
        b.Should().Equal(2000, 3000, 4400);
    }

    [Theory]
    [InlineData(1, 4, 1.0)] [InlineData(2, 4, 1.5)] [InlineData(4, 4, 2.0)]
    [InlineData(8, 4, 2.5)] [InlineData(12, 4, 3.0)] [InlineData(15, 4, 4.0)]
    public void Multiplier_2014_by_count(int count, int party, double mult) =>
        EncounterMath.Multiplier(count, party, DndVersion.Edition2014).Should().Be(mult);

    [Fact]
    public void Multiplier_shifts_up_for_small_party_and_down_for_large()
    {
        EncounterMath.Multiplier(2, 2, DndVersion.Edition2014).Should().Be(2.0);  // <3 PCs: 1.5 -> next step 2.0
        EncounterMath.Multiplier(2, 6, DndVersion.Edition2014).Should().Be(1.0);  // >=6 PCs: 1.5 -> prev step 1.0
    }

    [Fact]
    public void Multiplier_2024_is_always_one() =>
        EncounterMath.Multiplier(5, 4, DndVersion.Edition2024).Should().Be(1.0);

    [Fact]
    public void Classify_2014_uses_multiplier()
    {
        // party 4×L5 (thresholds Easy1000/Med2000/Hard3000/Deadly4400).
        // Three CR-3 monsters = 2100 XP, count 3 -> ×2 = 4200 adjusted -> ≥Hard(3000), <Deadly(4400) -> Hard.
        EncounterMath.Classify(2100, new[] { 5, 5, 5, 5 }, monsterCount: 3, DndVersion.Edition2014)
            .Should().Be(Difficulty.Hard);
    }

    [Fact]
    public void Classify_2024_no_multiplier()
    {
        // party 4×L5 (budgets 2000/3000/4400). 1400 raw XP -> below Low(2000) -> Trivial.
        EncounterMath.Classify(1400, new[] { 5, 5, 5, 5 }, monsterCount: 2, DndVersion.Edition2024)
            .Should().Be(Difficulty.Trivial);
    }

    [Fact]
    public void Classify_boundary_just_below_is_lower_band()
    {
        // 2024, party 4×L5: exactly Moderate(3000) => Medium; one below => Easy.
        EncounterMath.Classify(3000, new[] { 5,5,5,5 }, 1, DndVersion.Edition2024).Should().Be(Difficulty.Medium);
        EncounterMath.Classify(2999, new[] { 5,5,5,5 }, 1, DndVersion.Edition2024).Should().Be(Difficulty.Easy);
    }
}
```

- [ ] **Step 2: Run — verify fail.** `dotnet test --filter FullyQualifiedName~EncounterMathTests` → FAIL (types missing).
- [ ] **Step 3: Implement `EncounterMath` + `Difficulty`** — transcribe the FULL CR→XP (0..30), 2014 threshold table (levels 1..20 × 4), 2024 budget table (levels 1..20 × 3), and multiplier steps. Reuse `DndVersion`. Make the spot-checks pass; if a spot-check comment disagrees with the authoritative table, fix the comment.
- [ ] **Step 4: Run — verify pass; `dotnet build` 0/0.**
- [ ] **Step 5: Commit** `feat(encounters): EncounterMath core — CR→XP, party budgets, 2014 multiplier, classifier (both editions)`.

---

### Task 2: EncounterAssessor (rate)

**Files:**
- Create: `Features/Encounters/EncounterAssessment.cs` — `record MonsterRef(string Id, string Name, double Cr, int Xp)`; `record BandBoundary(Difficulty Band, int MinXp)`; `record EncounterAssessment(Difficulty Difficulty, int TotalMonsterXp, int AdjustedXp, IReadOnlyList<BandBoundary> Boundaries, IReadOnlyList<MonsterRef> Monsters)`.
- Create: `Features/Encounters/EncounterAssessor.cs`
- Test: `DndMcpAICsharpFun.Tests/Encounters/EncounterAssessorTests.cs`

**Interfaces / Produces:**
- `sealed class EncounterAssessor` with `EncounterAssessment Assess(IReadOnlyList<int> partyLevels, IReadOnlyList<MonsterRef> monsters, DndVersion ed)`:
  - `total = sum(monsters.Xp)`; `adjusted = round(total × Multiplier(count, partySize, ed))` (2024: adjusted==total).
  - `difficulty = EncounterMath.Classify(total, partyLevels, count, ed)`.
  - `boundaries` = the band→minXP list for this party (the thresholds/budgets), so callers can say "Deadly starts at N".

- [ ] **Step 1: Failing tests** — 2014: party 4×L5 + three CR-3 (700 each) → Hard, total 2100, adjusted 4200 (×2 for 3 monsters), boundaries include Deadly→4400. 2024: same three monsters → Easy (raw 2100 ≥ Low 2000, < Moderate 3000), adjusted==total. Assert difficulty + total + adjusted + a boundary value.
- [ ] **Step 2: Run fail → implement (pure over `EncounterMath`) → pass → build 0/0.**
- [ ] **Step 3: Commit** `feat(encounters): EncounterAssessor rates a party+monster list`.

---

### Task 3: EncounterGenerator (build) — fake retrieval

**Files:**
- Create: `Features/Encounters/IEncounterMonsterSource.cs` — `Task<IReadOnlyList<MonsterRef>> FindAsync(DndVersion ed, double crLte, double crGte, string? theme, bool srdOnly, int limit, CancellationToken ct)`.
- Create: `Features/Encounters/EncounterGenerator.cs` + `record BuiltEncounter(EncounterAssessment Assessment, bool FullyMatched, string? Note)`.
- Test: `DndMcpAICsharpFun.Tests/Encounters/EncounterGeneratorTests.cs`

**Interfaces / Produces:**
- `sealed class EncounterGenerator(IEncounterMonsterSource source, EncounterAssessor assessor)` with `Task<BuiltEncounter> BuildAsync(IReadOnlyList<int> partyLevels, Difficulty target, DndVersion ed, string? theme, double? crLte, double? crGte, CancellationToken ct)`:
  1. `budget = EncounterMath.PartyBudget(...)` → the target band's min XP.
  2. Derive a per-monster CR band from the target budget / party size; fetch candidates via `source.FindAsync`.
  3. Greedy select: add candidates toward the budget; **after each change, `assessor.Assess(...)` the current set** and stop when the assessed `Difficulty == target`. For 2014 this naturally handles the count→multiplier loop (assess, not raw XP). Bound the loop (max candidates, max iterations).
  4. Return `BuiltEncounter` with the Assessor's assessment. If the assessed band never reaches `target`, return the closest set with `FullyMatched=false` and a `Note`.

- [ ] **Step 1: Failing tests** with a FAKE `IEncounterMonsterSource`:
  - Build Hard for party 4×L5 with a fake pool of CR-3 (700xp) monsters → returned `Assessment.Difficulty == Hard` AND `assessor.Assess(returned monsters)` == Hard (build==rate).
  - Theme/CR passthrough: assert `FindAsync` was called with the given `theme` and a CR band.
  - Sparse fallback: fake returns 1 tiny monster → `FullyMatched==false`, `Note` set, no throw.
  - 2014 loop: adding monsters raises the multiplier; assert the final set's assessed band == target (not raw-XP band).
- [ ] **Step 2: Run fail → implement (bounded greedy; target the assessed band) → pass → build 0/0.**
- [ ] **Step 3: Commit** `feat(encounters): EncounterGenerator builds to a target band, rated by the shared Assessor`.

---

### Task 4: EncounterDesignService + real monster source + ownership

**Files:**
- Create: `Features/Encounters/EntitySearchMonsterSource.cs` (implements `IEncounterMonsterSource` over `IEntityRetrievalService`)
- Create: `Features/Encounters/EncounterDesignService.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs` (register the three)
- Test: `DndMcpAICsharpFun.Tests/Encounters/EncounterDesignServiceTests.cs`

**Interfaces / Produces:**
- `EntitySearchMonsterSource`: builds an `EntitySearchQuery` (theme text or a neutral term when null) with `EntityFilters(Type: EntityType.Monster, Edition: <ed>, Keyword: theme, CrNumericGte: crGte, CrNumericLte: crLte, Srd: srdOnly?)`, calls `IEntityRetrievalService.SearchAsync`, maps each result's `cr` → `MonsterRef` via `EncounterMath.CrToXp`. (Inspect `EntitySearchQuery`/`EntitySearchResult` shapes with Serena; reuse them, don't invent a new retrieval path.)
- `EncounterDesignService(EncounterAssessor, EncounterGenerator, HeroRepository, CampaignRepository, IEntityRetrievalService)`:
  - `Task<EncounterAssessment> RateForUserAsync(long userId, long? campaignId, IReadOnlyList<int>? partyLevels, IReadOnlyList<string> monsters, DndVersion ed, ct)` — resolve party (below), resolve each monster string (id or name) to a `MonsterRef` via entity lookup, `Assess`.
  - `Task<BuiltEncounter> BuildForUserAsync(long userId, long? campaignId, IReadOnlyList<int>? partyLevels, Difficulty target, DndVersion ed, string? theme, ct)` — resolve party, `Build`.
  - **Party resolution (shared private):** if `partyLevels` given → use it. Else if `campaignId` given → verify the campaign belongs to `userId` (fetch via `CampaignRepository` and check `UserId == userId`; throw `UnauthorizedAccessException` on mismatch/absent — mirror `GetSnapshotForUserAsync`), then party = each hero's level from `HeroRepository.GetByCampaignAsync(campaignId)` (hero level = `LatestSnapshot.Level` / `Sheet.Level`). Else → throw a clear `ArgumentException` ("supply campaignId or partyLevels").

- [ ] **Step 1: Failing tests** (fakes for repos + a fake/real assessor+generator):
  - Party from an owned campaign → uses the heroes' levels.
  - **Ownership negative:** `campaignId` owned by a different user → `RateForUserAsync`/`BuildForUserAsync` throws `UnauthorizedAccessException`; the other user's party is never used.
  - `partyLevels` override wins over campaign.
  - Neither supplied → `ArgumentException`.
  - `EntitySearchMonsterSource` maps a monster entity's `cr` to the right XP (small integration-ish test or a mapping unit test).
- [ ] **Step 2: Run fail → implement + DI-register → pass → build 0/0.**
- [ ] **Step 3: Commit** `feat(encounters): EncounterDesignService with campaign-ownership party resolution + entity-search monster source`.

---

### Task 5: Per-user MCP chat tools

**Files:**
- Modify: `Features/Chat/DndChatService.cs` (add two tools in the authenticated block; inject `EncounterDesignService`)
- Test: `DndMcpAICsharpFun.Tests/Chat/EncounterToolsTests.cs` (or extend the existing chat-tool test)

**Interfaces:**
- Inside `if (long.TryParse(idClaim, out var userId))`, add via `AIFunctionFactory.Create` (closing over `userId`):
  - `rate_encounter(long? campaignId, int[]? partyLevels, string[] monsters, string edition, CancellationToken ct)` → `encounterService.RateForUserAsync(userId, campaignId, partyLevels, monsters, ParseEdition(edition), ct)`.
  - `build_encounter(long? campaignId, string difficulty, string edition, string? theme, CancellationToken ct)` → `encounterService.BuildForUserAsync(userId, campaignId, partyLevels: null, ParseDifficulty(difficulty), ParseEdition(edition), theme, ct)`.
  - Descriptions: state edition is a parameter ("2014"|"2024"), party comes from the caller's campaign (`campaignId`) or explicit `partyLevels`, and that `build`'s difficulty is verified by the same math as `rate`.

- [ ] **Step 1: Failing test** — mirror the multiclass tool test: assert both tools are present only when authenticated, close over `userId`, and route to the service (a fake `EncounterDesignService` capturing the `userId`/args). Assert the tools are NOT added on the shared-key `Features/Mcp` surface (grep in the test or a structural assert).
- [ ] **Step 2: Run fail → wire tools + inject service → pass.** Grep-confirm no new HTTP route and `Features/Mcp/DndMcpTools.cs` unchanged (so `.http`/`.insomnia` need no update). `dotnet build` 0/0.
- [ ] **Step 3: Commit** `feat(encounters): per-user rate_encounter + build_encounter chat tools (SEC-08)`.

---

### Task 6: Verify + review

- [ ] **Step 1: Full build 0/0 + full suite** (`dotnet test`, Docker up) green.
- [ ] **Step 2: Drive it (per `verify`)** — if the stack is up, via the chat/MCP client `build_encounter` a Hard undead fight for a sample party, then `rate_encounter` those exact monsters → confirm the same band; confirm a foreign-campaign `campaignId` is rejected. Defer honestly if the stack is down (unit + ownership tests cover the logic).
- [ ] **Step 3: Whole-branch opus review** — cross-check every ADDED requirement (both-edition math incl. multiplier shifts, assessor context boundaries, **build==rate**, ownership rejection, sparse fallback). Address findings; then stop for the user's commit/archive directive.
