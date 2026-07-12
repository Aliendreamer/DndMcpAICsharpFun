# Encounter-design v2 (Swarms) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the encounter designer build boss-plus-minions swarms and rate hand-typed monster quantities, while the assessed set stays a flat monster list so the math and combat tracker are unchanged.

**Architecture:** Quantity is modeled only at the edges — a `MonsterQuantity(Name, Quantity)` input record for rating, and a `MonsterGrouping` display helper for chat/UI. The assessed set stays `IReadOnlyList<MonsterRef>` with repeats, so `EncounterMath`/`EncounterAssessor` need no change and each swarm member is its own combatant. The generator gains anchor-then-fill selection (one boss + strictly-cheaper minion multiples) by re-selecting candidates instead of removing them.

**Tech Stack:** .NET 10, C#, xUnit + FluentAssertions + NSubstitute, Microsoft.Extensions.AI (`AIFunctionFactory` chat tools), Blazor Server (Razor), Testcontainers.Qdrant for the integration test.

## Global Constraints

- Target framework `net10.0`; nullable enabled; implicit usings; **warnings-as-errors** (every project) — no warnings may be introduced.
- Central Package Management: never add a version to a `PackageReference`; versions live in `Directory.Packages.props`. This slice adds **no** new package.
- These are per-user **chat** tools (SEC-08): `userId` comes from the session-claim closure, never a tool argument. No HTTP route, no `.http`/`.insomnia`, no MCP shared-key surface, no EF migration.
- Work directly on `main`; commit each reviewed task straight to main.
- The build↔rate agreement invariant is sacred: a built encounter rated back through `EncounterAssessor` must yield the same `Difficulty`.
- Serena symbolic tools for all code reads/edits.

---

### Task 1: MonsterQuantity input record + MonsterGrouping display helper

**Files:**
- Create: `Features/Encounters/MonsterGrouping.cs` (holds `MonsterQuantity`, `MonsterCount`, and `MonsterGrouping`)
- Test: `DndMcpAICsharpFun.Tests/Encounters/MonsterGroupingTests.cs`

**Interfaces:**
- Consumes: `MonsterRef` (existing, `Features/Encounters/EncounterAssessment.cs`: `record MonsterRef(string Id, string Name, double Cr, int Xp, int InitiativeModifier = 0, int AverageHp = 0, string? HpFormula = null)`).
- Produces:
  - `public sealed record MonsterQuantity(string Name, int Quantity);`
  - `public sealed record MonsterCount(MonsterRef Monster, int Count);`
  - `public static IReadOnlyList<MonsterCount> MonsterGrouping.Group(IReadOnlyList<MonsterRef> monsters)` — group by `Id`, first-appearance order.
  - `public static string MonsterGrouping.Describe(IReadOnlyList<MonsterRef> monsters)` — `"1× Hobgoblin, 8× Goblin"`.

- [ ] **Step 1: Write the failing test**

```csharp
using DndMcpAICsharpFun.Features.Encounters;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Encounters;

public sealed class MonsterGroupingTests
{
    private static MonsterRef Goblin(string idSuffix = "") =>
        new($"mm.monster.goblin{idSuffix}", "Goblin", 0.25, 50);
    private static MonsterRef Hobgoblin() =>
        new("mm.monster.hobgoblin", "Hobgoblin", 0.5, 100);

    [Fact]
    public void Group_collapses_repeats_by_id_and_counts_them()
    {
        IReadOnlyList<MonsterRef> flat = [Hobgoblin(), Goblin(), Goblin(), Goblin()];

        var groups = MonsterGrouping.Group(flat);

        groups.Should().HaveCount(2);
        groups[0].Monster.Name.Should().Be("Hobgoblin");   // first-appearance order preserved
        groups[0].Count.Should().Be(1);
        groups[1].Monster.Name.Should().Be("Goblin");
        groups[1].Count.Should().Be(3);
    }

    [Fact]
    public void Group_keeps_distinct_ids_separate()
    {
        IReadOnlyList<MonsterRef> flat = [Goblin("-a"), Goblin("-b")];

        MonsterGrouping.Group(flat).Should().HaveCount(2);
    }

    [Fact]
    public void Group_of_empty_is_empty()
    {
        MonsterGrouping.Group([]).Should().BeEmpty();
    }

    [Fact]
    public void Describe_renders_counts_in_first_appearance_order()
    {
        IReadOnlyList<MonsterRef> flat = [Hobgoblin(), Goblin(), Goblin()];

        MonsterGrouping.Describe(flat).Should().Be("1× Hobgoblin, 2× Goblin");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~MonsterGroupingTests"`
Expected: FAIL — `MonsterGrouping`/`MonsterQuantity`/`MonsterCount` do not exist (compile error). Note: `dotnet` needs `dangerouslyDisableSandbox: true` (git-crypt Config).

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace DndMcpAICsharpFun.Features.Encounters;

/// <summary>A caller-supplied monster name plus how many of it to include (rate side).</summary>
public sealed record MonsterQuantity(string Name, int Quantity);

/// <summary>A monster and how many copies of it appear in a flat encounter list (display side).</summary>
public sealed record MonsterCount(MonsterRef Monster, int Count);

/// <summary>
/// Presentation-only grouping of a flat <see cref="MonsterRef"/> list (in which quantity is
/// expressed as repeated entries) into per-id counts, so chat text and the encounter panel can
/// render "8× Goblin" without the assessed set ever leaving its flat, per-combatant form.
/// </summary>
public static class MonsterGrouping
{
    /// <summary>Groups by <see cref="MonsterRef.Id"/>, preserving first-appearance order.</summary>
    public static IReadOnlyList<MonsterCount> Group(IReadOnlyList<MonsterRef> monsters)
    {
        ArgumentNullException.ThrowIfNull(monsters);

        var order = new List<string>();
        var byId = new Dictionary<string, (MonsterRef Monster, int Count)>();
        foreach (var m in monsters)
        {
            if (byId.TryGetValue(m.Id, out var existing))
            {
                byId[m.Id] = (existing.Monster, existing.Count + 1);
            }
            else
            {
                byId[m.Id] = (m, 1);
                order.Add(m.Id);
            }
        }

        return order.Select(id => new MonsterCount(byId[id].Monster, byId[id].Count)).ToList();
    }

    /// <summary>Renders the grouped counts as "1× Hobgoblin, 8× Goblin".</summary>
    public static string Describe(IReadOnlyList<MonsterRef> monsters) =>
        string.Join(", ", Group(monsters).Select(g => $"{g.Count}× {g.Monster.Name}"));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~MonsterGroupingTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add Features/Encounters/MonsterGrouping.cs DndMcpAICsharpFun.Tests/Encounters/MonsterGroupingTests.cs
git commit -m "feat(encounters): MonsterQuantity input + MonsterGrouping display helper"
```

---

### Task 2: Generator anchor-then-fill (boss + minions)

**Files:**
- Modify: `Features/Encounters/EncounterGenerator.cs` (the selection loop, lines ~113–181)
- Modify (test update): `DndMcpAICsharpFun.Tests/Encounters/EncounterGeneratorTests.cs` (the sparse-fallback test now expects a swarm)
- Test (new cases): `DndMcpAICsharpFun.Tests/Encounters/EncounterGeneratorTests.cs`

**Interfaces:**
- Consumes: `IEncounterMonsterSource.FindAsync`, `EncounterAssessor.Assess`, `EncounterMath.*` (all existing, unchanged).
- Produces: `EncounterGenerator.BuildAsync(...)` — same signature and `BuiltEncounter` return, but the returned `Assessment.Monsters` may now contain repeated entries (the swarm).

- [ ] **Step 1: Write the failing tests (add to EncounterGeneratorTests)**

```csharp
[Fact]
public async Task BuildAsync_builds_a_swarm_of_cheaper_minions_under_a_single_anchor()
{
    // 4×L5 (2014): Easy=1000, Medium=2000, Hard=3000, Deadly=4400.
    // Anchor CR5 = 1800 XP. Minions CR1/4 = 50 XP each (strictly cheaper).
    // 1 anchor alone: 1800 ×1.0 = 1800 → Medium (short of Hard=3000), so fill continues.
    // The fill re-selects the cheap minion in multiples toward Hard.
    IReadOnlyList<MonsterRef> pool =
    [
        new MonsterRef("mm.monster.boss", "Boss", 5, EncounterMath.CrToXp(5)),
        new MonsterRef("mm.monster.minion", "Minion", 0.25, EncounterMath.CrToXp(0.25)),
    ];
    var assessor = new EncounterAssessor();
    var generator = new EncounterGenerator(new FakeMonsterSource(pool), assessor);

    var result = await generator.BuildAsync(
        Party4L5, Difficulty.Hard, DndVersion.Edition2014, theme: null, crLte: null, crGte: null, CancellationToken.None);

    // Exactly one anchor (the single most expensive selection) plus multiple cheaper minions.
    result.Assessment.Monsters.Count(m => m.Id == "mm.monster.boss").Should().Be(1);
    result.Assessment.Monsters.Count(m => m.Id == "mm.monster.minion").Should().BeGreaterThan(1);
    // Build == rate for the repeated set.
    assessor.Assess(Party4L5, result.Assessment.Monsters, DndVersion.Edition2014)
        .Difficulty.Should().Be(result.Assessment.Difficulty);
}

[Fact]
public async Task BuildAsync_returns_a_solo_anchor_when_it_already_fills_the_band()
{
    // 4×L5 Easy=1000. A single CR5 (1800 ×1.0) already lands at/above Medium — but for an Easy
    // target the anchor pick is the highest-XP candidate that does NOT overshoot Easy.
    // CR1 (200 ×1.0 = 200) is Trivial; CR2 (450) Trivial; only when a single pick reaches the
    // band do we stop. Use a pool where one CR3 (700 ×1.0 = 700 < 1000 Easy) can't solo-fill, but
    // a party where the anchor alone suffices: 4×L1 Easy=100, one CR1/2 (100 ×1.0 = 100) == Easy.
    IReadOnlyList<int> party4L1 = [1, 1, 1, 1];
    IReadOnlyList<MonsterRef> pool =
    [
        new MonsterRef("mm.monster.anchor", "Anchor", 0.5, EncounterMath.CrToXp(0.5)), // 100 XP
        new MonsterRef("mm.monster.tiny", "Tiny", 0.125, EncounterMath.CrToXp(0.125)), // 25 XP
    ];
    var generator = new EncounterGenerator(new FakeMonsterSource(pool), new EncounterAssessor());

    var result = await generator.BuildAsync(
        party4L1, Difficulty.Easy, DndVersion.Edition2014, theme: null, crLte: null, crGte: null, CancellationToken.None);

    result.Assessment.Difficulty.Should().Be(Difficulty.Easy);
    result.FullyMatched.Should().BeTrue();
    result.Assessment.Monsters.Should().ContainSingle(m => m.Id == "mm.monster.anchor");
}

[Fact]
public async Task BuildAsync_stacks_a_uniform_swarm_when_no_candidate_is_cheaper_than_the_anchor()
{
    // Only one CR (all candidates same XP): there is no strictly-cheaper minion tier, so the fill
    // must re-select the anchor tier itself rather than dead-ending at a solo anchor.
    var source = new FakeMonsterSource(FiveCr3Monsters()); // all CR3 = 700 XP
    var assessor = new EncounterAssessor();
    var generator = new EncounterGenerator(source, assessor);

    var result = await generator.BuildAsync(
        Party4L5, Difficulty.Hard, DndVersion.Edition2014, theme: null, crLte: null, crGte: null, CancellationToken.None);

    result.Assessment.Monsters.Count.Should().BeGreaterThan(1); // stacked, not a solo anchor
    result.Assessment.Difficulty.Should().Be(Difficulty.Hard);
    assessor.Assess(Party4L5, result.Assessment.Monsters, DndVersion.Edition2014)
        .Difficulty.Should().Be(Difficulty.Hard);
}
```

- [ ] **Step 2: Update the existing sparse-fallback test to expect a swarm**

Replace the body of `BuildAsync_falls_back_to_the_closest_set_when_candidates_are_sparse` (currently asserts a single tiny monster) with the swarm expectation — re-selection now fills up to `MaxMonsters` with the only available monster:

```csharp
[Fact]
public async Task BuildAsync_falls_back_to_the_closest_set_when_candidates_are_sparse()
{
    // Only a CR1/8 (25 XP) monster is available and Hard is unreachable for a 4×L5 party even at
    // MaxMonsters. Re-selection fills with copies of that one monster up to the cap — the honest
    // "best achievable" swarm — rather than stopping at a single under-budget monster.
    IReadOnlyList<MonsterRef> pool = [new MonsterRef("mm.monster.tiny", "Tiny Monster", 0.125, EncounterMath.CrToXp(0.125))];
    var source = new FakeMonsterSource(pool);
    var generator = new EncounterGenerator(source, new EncounterAssessor());

    var result = await generator.BuildAsync(
        Party4L5, Difficulty.Hard, DndVersion.Edition2014, theme: null, crLte: null, crGte: null, CancellationToken.None);

    result.FullyMatched.Should().BeFalse();
    result.Note.Should().NotBeNullOrWhiteSpace();
    result.Assessment.Monsters.Should().HaveCount(15);                       // MaxMonsters cap
    result.Assessment.Monsters.Should().OnlyContain(m => m.Id == "mm.monster.tiny");
    result.PartyLevels.Should().Equal(Party4L5);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~EncounterGeneratorTests"`
Expected: the 3 new tests FAIL (generator still removes candidates → no repeats), and the rewritten sparse test FAILS (still returns a single monster).

- [ ] **Step 4: Change the selection loop in EncounterGenerator.BuildAsync**

Replace the block from `var selected = new List<MonsterRef>();` through the end of the `while` loop (currently lines ~113–170, up to and including the loop close, i.e. everything before `var fullyMatched = ...`) with:

```csharp
        var selected = new List<MonsterRef>();
        var current = assessor.Assess(partyLevels, selected, ed);

        // The XP of the first successful pick — the anchor (boss). Until it is set, the anchor is
        // still being chosen; after it is set, the fill phase prefers strictly-cheaper minions.
        int? anchorXp = null;

        // Tracks *why* the greedy loop stopped short of the target, so the fallback Note can explain
        // the real reason: either every eligible candidate would overshoot the target band, or the
        // pool was empty / the cap was hit without overshooting.
        var overshootBlocked = false;
        Difficulty? overshootBand = null;

        // Bounded greedy with re-selection (no candidate is removed, so the same monster can be
        // picked multiple times → quantity). The loop still runs at most MaxMonsters times because
        // each iteration either adds exactly one monster or breaks. Anchor-then-fill shape: the
        // first pick is the single highest-XP candidate that does not overshoot (the boss); after
        // that the eligible pool narrows to candidates strictly cheaper than the anchor (the
        // minions), falling back to the full pool when nothing is cheaper so a same-CR pool still
        // stacks into a uniform swarm.
        while (current.Difficulty != effectiveTarget && selected.Count < MaxMonsters)
        {
            IEnumerable<MonsterRef> eligible = anchorXp is null
                ? candidates
                : candidates.Any(c => c.Xp < anchorXp.Value)
                    ? candidates.Where(c => c.Xp < anchorXp.Value)
                    : candidates;

            MonsterRef? bestCandidate = null;
            EncounterAssessment? bestAssessment = null;
            Difficulty? roundOvershootBand = null;

            foreach (var candidate in eligible)
            {
                var trial = new List<MonsterRef>(selected) { candidate };
                var trialAssessment = assessor.Assess(partyLevels, trial, ed);

                if (trialAssessment.Difficulty > effectiveTarget)
                {
                    if (roundOvershootBand is null || trialAssessment.Difficulty < roundOvershootBand)
                    {
                        roundOvershootBand = trialAssessment.Difficulty;
                    }

                    continue; // this candidate would overshoot past the target band
                }

                if (bestAssessment is null || trialAssessment.AdjustedXp > bestAssessment.AdjustedXp)
                {
                    bestCandidate = candidate;
                    bestAssessment = trialAssessment;
                }
            }

            if (bestCandidate is null || bestAssessment is null)
            {
                // No monster could be added this round. If there were candidates at all, every one
                // would have overshot the target band; if the pool was empty, this is scarcity.
                overshootBlocked = candidates.Count > 0;
                overshootBand = roundOvershootBand;
                break;
            }

            selected.Add(bestCandidate);
            anchorXp ??= bestCandidate.Xp; // the first successful pick becomes the anchor
            current = bestAssessment;
        }
```

Also delete the now-unused `var remaining = new List<MonsterRef>(candidates);` line and the `remaining.Remove(bestCandidate);` line (both are removed by the replacement above). Leave the `var fullyMatched = ...` note block and `return` unchanged.

- [ ] **Step 5: Run the full Encounters test suite to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~DndMcpAICsharpFun.Tests.Encounters"`
Expected: PASS — the 3 new swarm tests, the rewritten sparse test, and all pre-existing generator tests (reaches-hard, targets-assessed-band, rejects-overshoot, widens-ceiling ×2, forwards-bounds, derives-band, throws-inverted) still green.

- [ ] **Step 6: Commit**

```bash
git add Features/Encounters/EncounterGenerator.cs DndMcpAICsharpFun.Tests/Encounters/EncounterGeneratorTests.cs
git commit -m "feat(encounters): generator builds boss+minions swarms via anchor-then-fill re-selection"
```

---

### Task 3: RateForUserAsync accepts structured quantity pairs

**Files:**
- Modify: `Features/Encounters/EncounterDesignService.cs` (`RateForUserAsync` signature + expansion)
- Modify (test update): `DndMcpAICsharpFun.Tests/Encounters/EncounterDesignServiceTests.cs` (existing rate calls → pairs; new expansion/clamp tests)

**Interfaces:**
- Consumes: `MonsterQuantity` (Task 1); existing private `ResolveMonsterAsync(string, CancellationToken) → Task<MonsterRef>`.
- Produces: `RateForUserAsync(long userId, long? campaignId, IReadOnlyList<int>? partyLevels, IReadOnlyList<MonsterQuantity> monsters, DndVersion ed, CancellationToken ct)` — each pair resolved once, repeated `Clamp(Quantity, 1, MaxCopiesPerType)` times into the flat list. Add `private const int MaxCopiesPerType = 100;`.

- [ ] **Step 1: Write the failing tests (add to EncounterDesignServiceTests)**

```csharp
[Fact]
public async Task RateForUserAsync_expands_a_quantity_pair_into_repeated_refs_with_one_lookup()
{
    var search = Substitute.For<IEntityRetrievalService>();
    search.GetByIdAsync("mm.monster.goblin", Arg.Any<CancellationToken>())
        .Returns(FullResultWithCr("mm.monster.goblin", "Goblin", "1/4"));
    var service = BuildService(search: search);

    var assessment = await service.RateForUserAsync(
        1, campaignId: null, partyLevels: [5],
        monsters: [new MonsterQuantity("mm.monster.goblin", 8)], DndVersion.Edition2014, CancellationToken.None);

    assessment.Monsters.Should().HaveCount(8);
    assessment.Monsters.Should().OnlyContain(m => m.Id == "mm.monster.goblin");
    // Resolved exactly once, then repeated — not looked up eight times.
    await search.Received(1).GetByIdAsync("mm.monster.goblin", Arg.Any<CancellationToken>());
}

[Fact]
public async Task RateForUserAsync_treats_a_non_positive_quantity_as_one()
{
    var search = Substitute.For<IEntityRetrievalService>();
    search.GetByIdAsync("mm.monster.ogre", Arg.Any<CancellationToken>())
        .Returns(FullResultWithCr("mm.monster.ogre", "Ogre", "3"));
    var service = BuildService(search: search);

    var assessment = await service.RateForUserAsync(
        1, campaignId: null, partyLevels: [5],
        monsters: [new MonsterQuantity("mm.monster.ogre", 0)], DndVersion.Edition2014, CancellationToken.None);

    assessment.Monsters.Should().ContainSingle();
}

[Fact]
public async Task RateForUserAsync_clamps_an_excessive_quantity_to_the_safety_maximum()
{
    var search = Substitute.For<IEntityRetrievalService>();
    search.GetByIdAsync("mm.monster.rat", Arg.Any<CancellationToken>())
        .Returns(FullResultWithCr("mm.monster.rat", "Rat", "0"));
    var service = BuildService(search: search);

    var assessment = await service.RateForUserAsync(
        1, campaignId: null, partyLevels: [5],
        monsters: [new MonsterQuantity("mm.monster.rat", 9999)], DndVersion.Edition2014, CancellationToken.None);

    assessment.Monsters.Should().HaveCount(100); // MaxCopiesPerType
}
```

- [ ] **Step 2: Update the existing rate tests that pass bare-string monsters to pairs**

In `EncounterDesignServiceTests`, change each `monsters: ["<id>"]` argument to `monsters: [new MonsterQuantity("<id>", 1)]` in these three tests (the `monsters: []` empty-list calls need no change — an empty collection expression still binds to `IReadOnlyList<MonsterQuantity>`):
- `RateForUserAsync_resolves_a_monster_by_id_and_reflects_its_xp` → `monsters: [new MonsterQuantity("mm.monster.ogre", 1)]`
- `RateForUserAsync_throws_ArgumentException_when_the_monster_is_unresolvable` → `monsters: [new MonsterQuantity("nonexistent", 1)]`
- `RateForUserAsync_throws_ArgumentException_not_ArgumentOutOfRangeException_for_an_off_table_cr` → `monsters: [new MonsterQuantity("mm.monster.off-table", 1)]`

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~EncounterDesignServiceTests"`
Expected: FAIL to compile — `RateForUserAsync` still takes `IReadOnlyList<string>`, so the `MonsterQuantity` calls don't bind.

- [ ] **Step 4: Change RateForUserAsync in EncounterDesignService**

Replace the `RateForUserAsync` method (lines ~22–41) with:

```csharp
    /// <summary>Maximum copies a single quantity pair may contribute, so one pair can't balloon the rated set.</summary>
    private const int MaxCopiesPerType = 100;

    public async Task<EncounterAssessment> RateForUserAsync(
        long userId,
        long? campaignId,
        IReadOnlyList<int>? partyLevels,
        IReadOnlyList<MonsterQuantity> monsters,
        DndVersion ed,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(monsters);

        var party = await ResolvePartyAsync(userId, campaignId, partyLevels, ct);

        var monsterRefs = new List<MonsterRef>();
        foreach (var pair in monsters)
        {
            var resolved = await ResolveMonsterAsync(pair.Name, ct);
            var copies = Math.Clamp(pair.Quantity, 1, MaxCopiesPerType);
            for (var i = 0; i < copies; i++)
            {
                monsterRefs.Add(resolved);
            }
        }

        return assessor.Assess(party, monsterRefs, ed);
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~EncounterDesignServiceTests"`
Expected: PASS — the 3 new tests plus all updated existing tests.

- [ ] **Step 6: Commit**

```bash
git add Features/Encounters/EncounterDesignService.cs DndMcpAICsharpFun.Tests/Encounters/EncounterDesignServiceTests.cs
git commit -m "feat(encounters): rate accepts structured {name,quantity} pairs, expands to flat refs"
```

---

### Task 4: Chat tools — rate pairs + grouped build echo

**Files:**
- Modify: `Features/Chat/DndChatService.cs` (`rate_encounter` param + `build_encounter` grouped projection)
- Modify (test update): `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs`

**Interfaces:**
- Consumes: `MonsterQuantity`, `MonsterGrouping.Group`, `MonsterCount` (Task 1); `EncounterDesignService.RateForUserAsync` (Task 3, pairs) and `BuildForUserAsync` (existing).
- Produces:
  - `rate_encounter` delegate param becomes `MonsterQuantity[] monsters` (bound from a JSON array of `{name, quantity}`); still returns `EncounterAssessment`.
  - `build_encounter` returns a new projection so the model sees grouped counts:
    `public sealed record BuiltEncounterView(Difficulty Difficulty, int TotalMonsterXp, int AdjustedXp, bool FullyMatched, string? Note, IReadOnlyList<int> PartyLevels, IReadOnlyList<MonsterCount> Groups);`
    Define this record in `Features/Encounters/MonsterGrouping.cs` (co-located with the display helper).

- [ ] **Step 1: Add BuiltEncounterView record to MonsterGrouping.cs**

Append to `Features/Encounters/MonsterGrouping.cs`:

```csharp
/// <summary>
/// The grouped, model-facing shape of a built encounter — the flat repeated monster list collapsed
/// to per-monster counts so the chat tool echoes "8× Goblin" instead of eight separate entries.
/// </summary>
public sealed record BuiltEncounterView(
    Difficulty Difficulty,
    int TotalMonsterXp,
    int AdjustedXp,
    bool FullyMatched,
    string? Note,
    IReadOnlyList<int> PartyLevels,
    IReadOnlyList<MonsterCount> Groups);
```

- [ ] **Step 2: Write/adjust the failing chat-tool tests**

In `DndChatServiceTests`, update the existing rate-routing test's `monsters` argument to the pair shape (empty array of pairs), and add a build-grouping test. The rate test's deserialize assertion stays (rate still returns `EncounterAssessment`):

```csharp
// In RateEncounterTool_routes_partyLevels_and_monsters_to_EncounterDesignService — change the args line:
var result = await tool.InvokeAsync(
    ToArgs(new { campaignId = (long?)null, partyLevels = new[] { 5 }, monsters = Array.Empty<object>(), edition = "2014" }),
    CancellationToken.None);
// (rest of the test unchanged: result is a JsonElement deserializing to an EncounterAssessment with empty Monsters)

[Fact]
public async Task RateEncounterTool_expands_quantity_pairs_into_repeated_monsters()
{
    var search = Substitute.For<IEntityRetrievalService>();
    search.GetByIdAsync("mm.monster.goblin", Arg.Any<CancellationToken>())
        .Returns(new EntityFullResult(new EntityEnvelope(
            "mm.monster.goblin", EntityType.Monster, "Goblin", "MM", "Edition2014", null,
            new FirstAppearance("MM", "Edition2014"), Array.Empty<Revision>(), Array.Empty<string>(),
            "", System.Text.Json.JsonDocument.Parse("""{"cr":"1/4"}""").RootElement)));
    var client = new FakeChatClient();
    var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(42),
        encounterService: BuildEncounterDesignService(search: search));

    await svc.SendAsync("Rate it", false, CancellationToken.None);
    var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "rate_encounter");

    var result = await tool.InvokeAsync(
        ToArgs(new { campaignId = (long?)null, partyLevels = new[] { 5 },
            monsters = new[] { new { name = "mm.monster.goblin", quantity = 8 } }, edition = "2014" }),
        CancellationToken.None);

    var assessment = ((JsonElement)result!).Deserialize<EncounterAssessment>(tool.JsonSerializerOptions);
    assessment!.Monsters.Should().HaveCount(8);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~DndChatServiceTests"`
Expected: FAIL — the new expansion test can't bind pairs to the still-`string[]` `rate_encounter` param; the updated routing test may fail to compile against the old shape.

- [ ] **Step 4: Reshape the rate_encounter tool and project the build_encounter result**

In `DndChatService.cs`, replace the `rate_encounter` registration (lines ~90–100) with the pair-typed param:

```csharp
            toolList.Add(AIFunctionFactory.Create(
                (long? campaignId, int[]? partyLevels, MonsterQuantity[] monsters, string edition, CancellationToken toolCt) =>
                    encounterService.RateForUserAsync(
                        userId, campaignId, partyLevels, monsters, ParseEdition(edition), toolCt),
                name: "rate_encounter",
                description: "Rate a combat encounter's difficulty (Trivial/Easy/Medium/Hard/Deadly) " +
                    "for the signed-in user's party. The party comes from the caller's own campaign " +
                    "(campaignId) or an explicit partyLevels list (one level per party member); " +
                    "campaignId wins ownership-checked access, partyLevels overrides it when supplied. " +
                    "monsters is a list of {name, quantity} pairs — name is an entity id or free-text " +
                    "name to look up, quantity is how many of it (e.g. eight goblins is one pair " +
                    "{name:\"goblin\", quantity:8}). edition is \"2014\" or \"2024\"."));
```

Replace the `build_encounter` registration (lines ~102–116) so the delegate projects the built encounter to the grouped view:

```csharp
            toolList.Add(AIFunctionFactory.Create(
                async (long? campaignId, string difficulty, string edition, string? theme,
                    double? maxCr, double? minCr, CancellationToken toolCt) =>
                {
                    var built = await encounterService.BuildForUserAsync(
                        userId, campaignId, partyLevels: null, ParseDifficulty(difficulty),
                        ParseEdition(edition), theme, crLte: maxCr, crGte: minCr, toolCt);
                    var a = built.Assessment;
                    return new BuiltEncounterView(
                        a.Difficulty, a.TotalMonsterXp, a.AdjustedXp, built.FullyMatched, built.Note,
                        built.PartyLevels, MonsterGrouping.Group(a.Monsters));
                },
                name: "build_encounter",
                description: "Build a combat encounter for a target difficulty " +
                    "(Trivial/Easy/Medium/Hard/Deadly) and optional theme, for the signed-in user's " +
                    "party (from the caller's own campaignId). Builds swarms — a strong anchor plus " +
                    "multiples of cheaper monsters — returned grouped as {monster, count} (e.g. one " +
                    "hobgoblin leading eight goblins). Rated by the same math as rate_encounter, so a " +
                    "built encounter and a subsequent rate_encounter call agree on its difficulty. " +
                    "edition is \"2014\" or \"2024\". Optional maxCr/minCr constrain the candidate " +
                    "monsters' CR range; when omitted, a sensible CR ceiling/floor is derived from the " +
                    "target difficulty band."));
```

Add `using DndMcpAICsharpFun.Features.Encounters;` to `DndChatService.cs` if `MonsterQuantity`/`MonsterGrouping`/`BuiltEncounterView` are not already in scope (they live in that namespace).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DndChatServiceTests"`
Expected: PASS — updated routing test, new expansion test, the unchanged presence/SEC-08/no-userId tests (the schema still exposes `monsters` without `userId`), and the build guard-throw test.

- [ ] **Step 6: Commit**

```bash
git add Features/Chat/DndChatService.cs Features/Encounters/MonsterGrouping.cs DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs
git commit -m "feat(chat): rate_encounter takes {name,quantity} pairs; build_encounter echoes grouped counts"
```

---

### Task 5: EncounterPanel grouped display

**Files:**
- Modify: `CompanionUI/Components/EncounterPanel.razor` (the monster line)

**Interfaces:**
- Consumes: `MonsterGrouping.Describe` (Task 1); `_built.Assessment.Monsters` (existing).
- Produces: no code interface; the built-encounter summary renders grouped counts. `OnBuilt` still forwards the flat `BuiltEncounter` (individual combatants) — unchanged.

- [ ] **Step 1: Change the monster summary line**

In `EncounterPanel.razor`, replace line 48:

```razor
            <p>Monsters: @string.Join(", ", a.Monsters.Select(m => m.Name))</p>
```

with the grouped rendering:

```razor
            <p>Monsters: @MonsterGrouping.Describe(a.Monsters)</p>
```

(`MonsterGrouping` is in the already-imported `DndMcpAICsharpFun.Features.Encounters` namespace — line 3 `@using`.) Leave `BuildAsync`/`OnBuilt`/`SaveAsync` untouched: the flat `_built.Assessment.Monsters` still flows to the tracker and the log payload.

- [ ] **Step 2: Build to verify the Razor compiles**

Run: `dotnet build`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add CompanionUI/Components/EncounterPanel.razor
git commit -m "feat(ui): EncounterPanel renders grouped swarm counts (8x Goblin)"
```

---

### Task 6: Real-Qdrant swarm build↔rate integration test

**Files:**
- Modify: `DndMcpAICsharpFun.Tests/Encounters/EncounterDesignIntegrationTests.cs` (add a swarm case reusing the existing fixture/seed)

**Interfaces:**
- Consumes: the existing `_sut` (`EncounterDesignService` over real Qdrant), `BuildForUserAsync`, `RateForUserAsync` (now pair-typed).
- Produces: no new interface; a test proving build↔rate agreement holds for a swarm (repeated monsters).

- [ ] **Step 1: Read the existing integration test to reuse its fixture and seeded monsters**

Run: `sed -n '1,140p' DndMcpAICsharpFun.Tests/Encounters/EncounterDesignIntegrationTests.cs`
Note the seeded monster ids/CRs and the party used, so the swarm assertion targets a band the seeded corpus can actually build.

- [ ] **Step 2: Add the swarm agreement test**

Add a test that builds toward a band requiring multiples for the seeded party, then rates the exact grouped result back and asserts the same `Difficulty`. Use the grouped counts to reconstruct the rate input as pairs:

```csharp
[Fact]
public async Task Swarm_build_and_rate_agree_over_real_qdrant_retrieval()
{
    IReadOnlyList<int> partyLevels = [5, 5, 5, 5];

    var built = await _sut.BuildForUserAsync(
        userId: 1, campaignId: null, partyLevels, Difficulty.Hard, DndVersion.Edition2014,
        theme: null, crLte: null, crGte: null, CancellationToken.None);

    // Rate the exact built set back as quantity pairs (grouped by id) — build == rate must hold
    // even when the built encounter contains repeated monsters.
    var pairs = MonsterGrouping.Group(built.Assessment.Monsters)
        .Select(g => new MonsterQuantity(g.Monster.Id, g.Count))
        .ToList();

    var rated = await _sut.RateForUserAsync(
        userId: 1, campaignId: null, partyLevels, pairs, DndVersion.Edition2014, CancellationToken.None);

    rated.Difficulty.Should().Be(built.Assessment.Difficulty);
    rated.Monsters.Should().HaveCount(built.Assessment.Monsters.Count); // pairs expanded back 1:1
}
```

If the existing test's `RateForUserAsync` calls elsewhere in this file pass bare strings, update them to `MonsterQuantity` pairs (same mechanical change as Task 3 Step 2).

- [ ] **Step 3: Run the integration test (Docker + Qdrant required)**

Run: `dotnet test --filter "FullyQualifiedName~EncounterDesignIntegrationTests"`
Expected: PASS — build↔rate agree for the swarm (Docker must be running; the test uses `Testcontainers.Qdrant`).

- [ ] **Step 4: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Encounters/EncounterDesignIntegrationTests.cs
git commit -m "test(encounters): swarm build==rate agreement over real Qdrant"
```

---

### Task 7: Full verification + live swarm smoke

**Files:** none (verification only)

- [ ] **Step 1: Build clean**

Run: `dotnet build`
Expected: 0 warnings, 0 errors (warnings-as-errors).

- [ ] **Step 2: Full test suite green**

Run: `dotnet test`
Expected: all tests pass (Docker up for Postgres + Qdrant containers). Record the new total (was 1253/1253 before this slice; expect a higher count with the added tests).

- [ ] **Step 3: Rebuild the app container so the smoke tests current code (not a stale image)**

Run: `docker compose build app && docker compose up -d app`
Expected: app rebuilt and running on :5101. (A stale image silently screenshots old markup — this is the dev-flow UI gate.)

- [ ] **Step 4: Live Playwright swarm smoke on the Scratch page**

Drive `/scratch` (sign in as `test`): enter a party (size 4, level 5), Build a Hard encounter, and confirm:
- The monster summary renders **grouped counts** (e.g. "8× Goblin" or "1× <Anchor>, N× <Minion>"), not a flat repeated list.
- No horizontal overflow at desktop (1280) and mobile (390) widths.
- If a combat tracker is reachable from a campaign table build, confirm the swarm lands as **individual combatants** (N separate rows), not one grouped row.

Expected: grouped display present; individual combatants in the tracker; no overflow. Capture screenshots.

- [ ] **Step 5: Final whole-branch review + roadmap refresh**

Request a final opus whole-branch review (per dev-flow). On READY, refresh the `companion_roadmap` Serena memory with the shipped swarm slice and the remaining frontier candidates.
