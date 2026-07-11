# Combat Fight Fidelity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Backend tasks are TDD with real-Postgres tests; the UI task is verified by build + Playwright screenshots (controller-run). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Make the initiative tracker fight-ready: encounter-drafted monsters arrive with real HP (average or app-rolled), damage/heal applies by N, and removing the current combatant re-anchors the turn to the next-in-order.

**Architecture:** Monster HP mirrors the shipped monster-Dex path — `MonsterRef` carries it, read at the encounter (Qdrant) layer, consumed by `CombatService.DraftMonstersAsync`. The turn re-anchor is a repository change to `RemoveCombatantAsync`. Damage/heal-by-N is UI over the existing `AdjustHpAsync`.

**Tech Stack:** .NET 10, EF Core + PostgreSQL, Blazor Server, xunit + Testcontainers.PostgreSql, Playwright.

## Global Constraints

- **Presentational + service/repo logic only** — NO new table/migration, NO DI change, NO new HTTP route/MCP tool; do NOT touch `DndMcpAICsharpFun.http` / `dnd-mcp-api.insomnia.json`. Full `dotnet test` suite MUST stay green.
- **warnings-as-errors** (build 0/0). `dotnet` runs need `dangerouslyDisableSandbox: true`. Docker up for the Postgres Testcontainer.
- **Serena for all `.cs`/`.razor` edits**; built-in Read/Edit forbidden on code files. Every implementer/reviewer prompt includes the CRITICAL-Serena block + `initial_instructions`.
- `MonsterRef`'s new fields are **defaulted** (additive) — no other `new MonsterRef(...)` site may break.
- Monster HP is read from the entity at the encounter layer only — the combat tracker gains NO Qdrant dependency.
- `DiceExpression.TryParse(string, out DiceExpression, out string?)` parses `NdX±K`; `roller.Roll(expr).Total` rolls it. `CombatantOrder.Sort(IEnumerable<Combatant>)` returns the shared initiative order.

## File Structure

- Modify `Features/Encounters/EncounterAssessment.cs` — `MonsterRef` +`AverageHp`/`HpFormula`.
- Modify `Features/Encounters/EntitySearchMonsterSource.cs` — add `MonsterHp.TryRead`; populate at `FindAsync`.
- Modify `Features/Encounters/EncounterDesignService.cs` — populate at the two `ResolveMonsterAsync` sites.
- Modify `Features/Combat/CombatService.cs` — `DraftMonstersAsync` `rollHp` + HP; `ResolveMonsterHp` helper.
- Modify `Features/Combat/CombatRepository.cs` — `RemoveCombatantAsync` re-anchor.
- Modify `CompanionUI/Components/InitiativeTracker.razor` — roll-HP toggle + damage/heal-by-N.
- Tests under `DndMcpAICsharpFun.Tests/Encounters/` and `DndMcpAICsharpFun.Tests/Combat/`.

---

### Task 1: MonsterRef HP + MonsterHp reader

**Files:** Modify `Features/Encounters/EncounterAssessment.cs`, `Features/Encounters/EntitySearchMonsterSource.cs`, `Features/Encounters/EncounterDesignService.cs`; Test `DndMcpAICsharpFun.Tests/Encounters/MonsterHpTests.cs`.

**Interfaces:**
- Produces: `MonsterRef(string Id, string Name, double Cr, int Xp, int InitiativeModifier = 0, int AverageHp = 0, string? HpFormula = null)`; `internal static class MonsterHp { bool TryRead(JsonElement fields, out int average, out string? formula) }`.

- [ ] **Step 1: Write the failing helper test**

`DndMcpAICsharpFun.Tests/Encounters/MonsterHpTests.cs`:

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Features.Encounters;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Encounters;

public sealed class MonsterHpTests
{
    private static JsonElement Fields(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Reads_average_and_formula()
    {
        MonsterHp.TryRead(Fields("{\"hp\":{\"average\":7,\"formula\":\"2d6\"}}"), out var avg, out var formula)
            .Should().BeTrue();
        avg.Should().Be(7);
        formula.Should().Be("2d6");
    }

    [Fact]
    public void Reads_average_when_formula_absent()
    {
        MonsterHp.TryRead(Fields("{\"hp\":{\"average\":22}}"), out var avg, out var formula).Should().BeTrue();
        avg.Should().Be(22);
        formula.Should().BeNull();
    }

    [Fact]
    public void Missing_hp_returns_false()
    {
        MonsterHp.TryRead(Fields("{\"cr\":\"1/4\"}"), out var avg, out var formula).Should().BeFalse();
        avg.Should().Be(0);
        formula.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run it — expect FAIL** (`MonsterHp` doesn't exist).

Run: `dotnet test --filter FullyQualifiedName~MonsterHpTests` (dangerouslyDisableSandbox).

- [ ] **Step 3: Extend MonsterRef**

In `Features/Encounters/EncounterAssessment.cs`:

```csharp
public sealed record MonsterRef(string Id, string Name, double Cr, int Xp, int InitiativeModifier = 0, int AverageHp = 0, string? HpFormula = null);
```

- [ ] **Step 4: Add the `MonsterHp` helper** (in `EntitySearchMonsterSource.cs`, next to `MonsterDex`):

```csharp
/// <summary>Reads a monster's HP from its entity fields (<c>MonsterFields.hp.average</c> + optional
/// <c>hp.formula</c>). Returns false when no usable average is present (caller defaults to 0).</summary>
internal static class MonsterHp
{
    public static bool TryRead(JsonElement fields, out int average, out string? formula)
    {
        average = 0;
        formula = null;
        if (fields.ValueKind != JsonValueKind.Object
            || !fields.TryGetProperty("hp", out var hpEl) || hpEl.ValueKind != JsonValueKind.Object)
            return false;
        if (!hpEl.TryGetProperty("average", out var avgEl)
            || avgEl.ValueKind != JsonValueKind.Number || !avgEl.TryGetInt32(out average))
            return false;
        if (hpEl.TryGetProperty("formula", out var fEl) && fEl.ValueKind == JsonValueKind.String)
            formula = fEl.GetString();
        return true;
    }
}
```

- [ ] **Step 5: Populate at all 3 MonsterRef sites**

`EntitySearchMonsterSource.FindAsync` — where it does `monsters.Add(new MonsterRef(hit.Id, hit.Name, cr, xp, initMod));` (the `initMod` from `MonsterDex`), add before it and extend the constructor:

```csharp
            MonsterHp.TryRead(hit.Fields, out var avgHp, out var hpFormula);
            monsters.Add(new MonsterRef(hit.Id, hit.Name, cr, xp, initMod, avgHp, hpFormula));
```

`EncounterDesignService.ResolveMonsterAsync` — both `new MonsterRef(...)` sites (the `byId.Envelope.Fields` one and the `hit.Fields` one) gain the same two-line pattern using the matching fields source, extending each constructor with `, avgHp, hpFormula` (use locally-unique names if `m`/`avgHp` collide in a block).

- [ ] **Step 6: Run tests to green + build**

Run: `dotnet test --filter FullyQualifiedName~MonsterHpTests` (3/3) then `dotnet build` 0/0.

- [ ] **Step 7: Commit**

```bash
git add Features/Encounters/EncounterAssessment.cs Features/Encounters/EntitySearchMonsterSource.cs Features/Encounters/EncounterDesignService.cs DndMcpAICsharpFun.Tests/Encounters/MonsterHpTests.cs
git commit -m "feat(encounters): MonsterRef carries stat-block HP (average + formula)"
```

---

### Task 2: DraftMonstersAsync sets monster HP

**Files:** Modify `Features/Combat/CombatService.cs`; Test `DndMcpAICsharpFun.Tests/Combat/CombatServiceDraftingTests.cs`.

**Interfaces:**
- Consumes: `MonsterRef.AverageHp`/`HpFormula` (Task 1); `DiceExpression.TryParse`; `roller` (existing `DiceRoller` field).
- Produces: `DraftMonstersAsync(long combatId, long campaignId, long userId, IReadOnlyList<MonsterRef> monsters, bool rollHp)`.

- [ ] **Step 1: Write the failing tests** (add to `CombatServiceDraftingTests`; `MinRandomSource` → d20/each die = its min already exists in the file):

```csharp
    [Fact]
    public async Task Draft_monsters_uses_average_hp_when_roll_off()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        var combatId = (await _combats.StartAsync(userId, campaignId, "Fight", DndVersion.Edition2014))!.Value;

        await NewService().DraftMonstersAsync(combatId, campaignId, userId,
            new[] { new MonsterRef("mm.monster.goblin", "Goblin", 0.25, 50, 0, 7, "2d6") }, rollHp: false);

        var goblin = (await _combats.GetCombatantsAsync(combatId, campaignId, userId)).Single();
        goblin.MaxHp.Should().Be(7);
        goblin.CurrentHp.Should().Be(7);
    }

    [Fact]
    public async Task Draft_monsters_rolls_formula_when_roll_on()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        var combatId = (await _combats.StartAsync(userId, campaignId, "Fight", DndVersion.Edition2014))!.Value;

        // MinRandomSource → each d6 = 1, so "2d6" rolls 2. Distinct from the average (7).
        await NewService().DraftMonstersAsync(combatId, campaignId, userId,
            new[] { new MonsterRef("mm.monster.goblin", "Goblin", 0.25, 50, 0, 7, "2d6") }, rollHp: true);

        var goblin = (await _combats.GetCombatantsAsync(combatId, campaignId, userId)).Single();
        goblin.MaxHp.Should().Be(2);
        goblin.CurrentHp.Should().Be(2);
    }

    [Fact]
    public async Task Draft_monsters_falls_back_to_average_when_formula_missing()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        var combatId = (await _combats.StartAsync(userId, campaignId, "Fight", DndVersion.Edition2014))!.Value;

        await NewService().DraftMonstersAsync(combatId, campaignId, userId,
            new[] { new MonsterRef("x", "Ogre", 2, 450, 0, 59, null) }, rollHp: true);

        (await _combats.GetCombatantsAsync(combatId, campaignId, userId)).Single().MaxHp.Should().Be(59);
    }
```

Also update the file's EXISTING `Draft_monsters_auto_rolls_initiative_over_the_injected_rng` / `Draft_monsters_applies_the_monster_initiative_modifier` calls to pass `rollHp: false` (the new required arg).

- [ ] **Step 2: Run — expect FAIL** (arity/behavior). `dotnet test --filter FullyQualifiedName~CombatServiceDraftingTests`.

- [ ] **Step 3: Implement**

In `Features/Combat/CombatService.cs`, replace `DraftMonstersAsync` and add a helper:

```csharp
    public async Task DraftMonstersAsync(
        long combatId, long campaignId, long userId, IReadOnlyList<MonsterRef> monsters, bool rollHp)
    {
        foreach (var monster in monsters)
        {
            var maxHp = ResolveMonsterHp(monster, rollHp);
            await combats.AddCombatantAsync(combatId, campaignId, userId, new Combatant
            {
                Name = monster.Name,
                IsPlayer = false,
                InitiativeModifier = monster.InitiativeModifier,
                InitiativeRoll = RollInitiative(monster.InitiativeModifier),
                MaxHp = maxHp,
                CurrentHp = maxHp,
            });
        }
    }

    // Rolled HP when requested and a formula parses; otherwise the book average (0 if the stat block had neither).
    private int ResolveMonsterHp(MonsterRef monster, bool rollHp)
    {
        if (rollHp && !string.IsNullOrWhiteSpace(monster.HpFormula)
            && DiceExpression.TryParse(monster.HpFormula.Replace(" ", ""), out var expr, out _))
        {
            return roller.Roll(expr).Total;
        }
        return monster.AverageHp;
    }
```

- [ ] **Step 4: Run to green** — `dotnet test --filter FullyQualifiedName~CombatServiceDraftingTests` + `dotnet build` 0/0.

- [ ] **Step 5: Commit**

```bash
git add Features/Combat/CombatService.cs DndMcpAICsharpFun.Tests/Combat/CombatServiceDraftingTests.cs
git commit -m "feat(combat): DraftMonstersAsync sets monster HP (average or rolled formula)"
```

---

### Task 3: RemoveCombatantAsync re-anchors the current turn

**Files:** Modify `Features/Combat/CombatRepository.cs`; Test `DndMcpAICsharpFun.Tests/Combat/CombatRepositoryCombatantTests.cs`.

- [ ] **Step 1: Write the failing tests** (add to `CombatRepositoryCombatantTests`; `Monster(name, init)` helper + `SeedCombatAsync` exist):

```csharp
    [Fact]
    public async Task Removing_the_current_combatant_moves_the_turn_to_the_next_in_order()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        var idA = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("A", 20));
        var idB = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("B", 15));
        var idC = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("C", 10));

        // Advance to B (order A,B,C; null start = A is current, one advance → B).
        await _repo.AdvanceTurnAsync(combatId, campaignId, userId);
        (await _repo.GetByIdAsync(combatId, campaignId, userId))!.CurrentTurnCombatantId.Should().Be(idB);

        await _repo.RemoveCombatantAsync(idB, combatId, campaignId, userId);

        var combat = await _repo.GetByIdAsync(combatId, campaignId, userId);
        combat!.CurrentTurnCombatantId.Should().Be(idC);   // next-in-order, NOT the top (idA)
        combat.Round.Should().Be(1);
    }

    [Fact]
    public async Task Removing_the_current_last_combatant_wraps_to_the_first()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        var idA = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("A", 20));
        var idB = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("B", 10));
        await _repo.AdvanceTurnAsync(combatId, campaignId, userId); // A -> B (last)

        await _repo.RemoveCombatantAsync(idB, combatId, campaignId, userId);

        (await _repo.GetByIdAsync(combatId, campaignId, userId))!.CurrentTurnCombatantId.Should().Be(idA);
    }

    [Fact]
    public async Task Removing_the_only_combatant_clears_the_current_turn()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        var idA = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("A", 20));
        await _repo.AdvanceTurnAsync(combatId, campaignId, userId); // 1 combatant: wraps, still A current

        await _repo.RemoveCombatantAsync(idA, combatId, campaignId, userId);

        (await _repo.GetByIdAsync(combatId, campaignId, userId))!.CurrentTurnCombatantId.Should().BeNull();
    }

    [Fact]
    public async Task Removing_a_non_current_combatant_leaves_the_turn_untouched()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        var idA = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("A", 20));
        var idB = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("B", 10));
        await _repo.AdvanceTurnAsync(combatId, campaignId, userId); // current = B

        await _repo.RemoveCombatantAsync(idA, combatId, campaignId, userId); // remove the NON-current A

        (await _repo.GetByIdAsync(combatId, campaignId, userId))!.CurrentTurnCombatantId.Should().Be(idB);
    }
```

- [ ] **Step 2: Run — expect FAIL** (`RemoveCombatantAsync` doesn't re-anchor). `dotnet test --filter FullyQualifiedName~CombatRepositoryCombatantTests`.

- [ ] **Step 3: Implement** — replace `RemoveCombatantAsync`:

```csharp
    public async Task RemoveCombatantAsync(long combatantId, long combatId, long campaignId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var combat = await db.Combats
            .FirstOrDefaultAsync(c => c.Id == combatId && c.CampaignId == campaignId && c.UserId == userId);
        if (combat is null) return;

        // If the combatant being removed is the current turn, re-anchor to the next-in-order using the
        // PRE-removal sorted order, so the turn marker never points at a removed combatant.
        if (combat.CurrentTurnCombatantId == combatantId)
        {
            var combatants = await db.Combatants.Where(x => x.CombatId == combatId).ToListAsync();
            var ordered = CombatantOrder.Sort(combatants);
            var idx = -1;
            for (var i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Id == combatantId) { idx = i; break; }
            }
            combat.CurrentTurnCombatantId = (idx >= 0 && ordered.Count > 1)
                ? ordered[(idx + 1) % ordered.Count].Id   // next-in-order; wraps to first if it was last
                : null;                                    // it was the only combatant
        }

        await db.Combatants.Where(x => x.Id == combatantId && x.CombatId == combatId).ExecuteDeleteAsync();
        await db.SaveChangesAsync();
    }
```

(`CombatantOrder` is in the same `DndMcpAICsharpFun.Features.Combat` namespace — no new using.)

- [ ] **Step 4: Run to green** — `dotnet test --filter FullyQualifiedName~CombatRepositoryCombatantTests` (existing + 4 new) + `dotnet build` 0/0.

- [ ] **Step 5: Commit**

```bash
git add Features/Combat/CombatRepository.cs DndMcpAICsharpFun.Tests/Combat/CombatRepositoryCombatantTests.cs
git commit -m "fix(combat): removing the current combatant re-anchors the turn to the next-in-order"
```

---

### Task 4: InitiativeTracker UI — roll-HP toggle + damage/heal-by-N

**Files:** Modify `CompanionUI/Components/InitiativeTracker.razor` (Serena); `wwwroot/app.css` (styling, direct edit).

- [ ] **Step 1: Roll-HP toggle**

Add a `@code` field `private bool _rollMonsterHp;`. In the tracker add area (near "+ Party" / the add-combatant row), add a labelled checkbox:

```razor
<label class="roll-hp-toggle"><input type="checkbox" @bind="_rollMonsterHp" /> 🎲 Roll monster HP</label>
```

Update `AddMonstersAsync` to pass it through: change the `DraftMonstersAsync(_combat.Id, CampaignId, UserId, monsters)` call to `DraftMonstersAsync(_combat.Id, CampaignId, UserId, monsters, _rollMonsterHp)`.

- [ ] **Step 2: Damage/heal-by-N**

Add per-row amount state + helpers in `@code`:

```csharp
    private readonly Dictionary<long, int> _hpAmount = new();
    private int HpAmount(Combatant c) => _hpAmount.TryGetValue(c.Id, out var n) ? n : 1;
    private void SetHpAmount(Combatant c, string? raw) => _hpAmount[c.Id] = int.TryParse(raw, out var n) && n > 0 ? n : 1;
```

In the combatant HP controls, keep the `−`/`+` buttons but make them apply the amount, and add a compact number field between them:

```razor
<button @onclick="() => AdjustHpAsync(c, -HpAmount(c))">−</button>
@c.CurrentHp / <input type="number" class="c-maxhp" value="@c.MaxHp" @onchange="e => SetMaxHpAsync(c, e.Value?.ToString())" />
<input type="number" class="c-hpamt" min="1" value="@HpAmount(c)" @onchange="e => SetHpAmount(c, e.Value?.ToString())" />
<button @onclick="() => AdjustHpAsync(c, HpAmount(c))">+</button>
```

(`AdjustHpAsync` already clamps to `0..MaxHp` and persists — no repo change. N defaults to 1, so leaving the field alone is identical to today.)

- [ ] **Step 3: Style** — in `wwwroot/app.css`, style `.c-hpamt` (narrow, mono, ~44px) and `.roll-hp-toggle` (compact, muted, token colors) to match the tracker. Keep the row from overflowing.

- [ ] **Step 4: Build** — `dotnet build` 0/0. (Controller rebuilds the container + screenshots; you don't run the app.)

- [ ] **Step 5: Commit**

```bash
git add CompanionUI/Components/InitiativeTracker.razor wwwroot/app.css
git commit -m "feat(ui): tracker roll-HP toggle + damage/heal-by-N controls"
```

---

### Task 5: Verify + review

- [ ] **Step 1** — `dotnet build` 0/0; **full `dotnet test`** suite green (incl. Tasks 1–3 tests). Confirm `git diff --name-only` shows NO `.http`/`.insomnia`, no `Migrations/`, no schema change; confirm no other `new MonsterRef(...)` site broke (search).
- [ ] **Step 2** — Rebuild the dev container; Playwright-screenshot the tracker: build an encounter with **roll-HP OFF** (monsters show book average HP, not 0), then **ON** (rolled), apply **damage-by-N** on a combatant, and **remove the current combatant** (turn moves to the next-in-order, still highlighted); desktop + mobile, no overflow.
- [ ] **Step 3** — Final whole-branch review (opus): HP drafting + fallback correct; remove-current re-anchor by identity (no off-by-one, `Round` untouched, non-current removals untouched); `DraftMonstersAsync` ripple handled; MODIFIED deltas match shipped behavior; no scope creep (no `.cs` beyond the named files, no `.http`/migration).

## Self-Review

- **Spec coverage:** monster HP average+rolled+fallback (T1–T2), remove-current re-anchor + wrap + clear + non-current-untouched (T3), roll toggle + damage/heal-by-N (T4). Both MODIFIED requirements' new scenarios map to tests.
- **Placeholder scan:** backend tasks have complete code + tests; the UI task has concrete markup + the screenshot loop.
- **Type consistency:** `DraftMonstersAsync(..., bool rollHp)` matches between T2 and T4's `AddMonstersAsync` call; `MonsterRef(Id,Name,Cr,Xp,InitiativeModifier,AverageHp,HpFormula)` matches between T1 and T2's test constructors.
