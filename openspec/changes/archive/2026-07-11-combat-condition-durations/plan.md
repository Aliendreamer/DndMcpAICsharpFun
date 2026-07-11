# Combat Condition Durations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Backend tasks are TDD with real-Postgres tests; the UI task is verified by build + Playwright screenshots (controller-run). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Give each combatant condition an optional per-condition rounds-remaining that ticks down on a round rollover and auto-expires — indefinite when unset.

**Architecture:** The per-combatant conditions list becomes `List<ConditionTimer>` in the same `ConditionsJson` column (no migration; backward-compatible read of the old string-array). The tick lives in `AdvanceTurnAsync`'s existing round-rollover branch (one atomic `SaveChanges`). The UI adds a per-chip rounds field.

**Tech Stack:** .NET 10, EF Core + PostgreSQL, Blazor Server, xunit + Testcontainers.PostgreSql, Playwright.

## Global Constraints

- **Presentational + combat model/logic only** — NO new table/migration, NO DI change, NO new HTTP route/MCP tool; do NOT touch `DndMcpAICsharpFun.http`/`dnd-mcp-api.insomnia.json`. Full `dotnet test` suite MUST stay green.
- **warnings-as-errors** (build 0/0). `dotnet` runs need `dangerouslyDisableSandbox: true`. Docker up for the Postgres Testcontainer.
- **Serena for all `.cs`/`.razor` edits**; built-in Read/Edit forbidden on code files. (After a Serena `replace_symbol_body` on a test class, eyeball that `[Fact]`/`[Theory]` attributes survived — they can be dropped, tripping `xUnit1013`.)
- The `ConditionsJson` column is REUSED — a JSON *shape* change only. Old rows (`["Poisoned"]`) MUST read as indefinite timers. The serializer always writes the new object shape.
- **Each task must build 0/0** — Task 1 is the atomic behavior-preserving retype (do the WHOLE ripple in one task so nothing is left red); Tasks 2–3 add features on top.
- The tick is per-round, all-combatants; it stays inside `AdvanceTurnAsync`'s single `SaveChangesAsync` (atomic — no execution-strategy transaction needed for one SaveChanges).

## File Structure

- Modify `Features/Combat/Combatant.cs` — `ConditionTimer` record; `Conditions` helper retype; `CombatantConditions` (de)serialize both shapes.
- Modify `Features/Combat/CombatRepository.cs` — `UpdateCombatantAsync` conditions param → timers (Task 1); `AdvanceTurnAsync` tick/expire on rollover (Task 2).
- Modify `CompanionUI/Components/InitiativeTracker.razor` — timer-typed condition uses (Task 1); per-chip rounds field (Task 3); `wwwroot/app.css` chip-rounds styling (Task 3).
- Tests under `DndMcpAICsharpFun.Tests/Combat/`.

---

### Task 1: ConditionTimer model + atomic behavior-preserving retype

Introduce `ConditionTimer` and retype every use of `Conditions` (`Condition` → `ConditionTimer`) in ONE task so the build stays green. Behavior is identical (all conditions indefinite; no rounds UI yet). Only the type + JSON shape change.

**Files:** Modify `Features/Combat/Combatant.cs`, `Features/Combat/CombatRepository.cs` (`UpdateCombatantAsync` param), `CompanionUI/Components/InitiativeTracker.razor` (its existing condition uses); Test `DndMcpAICsharpFun.Tests/Combat/CombatantConditionsTests.cs`.

**Interfaces:**
- Produces: `sealed record ConditionTimer(Condition Condition, int? RoundsRemaining = null)`; `Combatant.Conditions : IReadOnlyList<ConditionTimer>`; `CombatantConditions.Serialize(IReadOnlyList<ConditionTimer>)`/`Deserialize(string)`; `UpdateCombatantAsync(..., IReadOnlyList<ConditionTimer> conditions)`.

- [ ] **Step 1: Write the failing (de)serialize tests** (extend `CombatantConditionsTests`; update any existing case that assumed a `List<Condition>` shape):

```csharp
    [Fact]
    public void New_shape_round_trips_timed_and_indefinite()
    {
        var json = CombatantConditions.Serialize(new[]
        {
            new ConditionTimer(Condition.Poisoned, 2),
            new ConditionTimer(Condition.Prone, null),
        });
        var back = CombatantConditions.Deserialize(json);
        back.Should().HaveCount(2);
        back.Single(t => t.Condition == Condition.Poisoned).RoundsRemaining.Should().Be(2);
        back.Single(t => t.Condition == Condition.Prone).RoundsRemaining.Should().BeNull();
    }

    [Fact]
    public void Legacy_string_array_reads_as_indefinite()
    {
        var back = CombatantConditions.Deserialize("[\"Poisoned\",\"Prone\"]");
        back.Select(t => t.Condition).Should().BeEquivalentTo(new[] { Condition.Poisoned, Condition.Prone });
        back.Should().OnlyContain(t => t.RoundsRemaining == null);
    }

    [Fact]
    public void Empty_reads_as_no_conditions()
    {
        CombatantConditions.Deserialize("[]").Should().BeEmpty();
        CombatantConditions.Deserialize("").Should().BeEmpty();
    }
```

- [ ] **Step 2: Run — expect FAIL** (`ConditionTimer` missing). `dotnet test --filter FullyQualifiedName~CombatantConditionsTests`.

- [ ] **Step 3: Model + helper** in `Features/Combat/Combatant.cs`:

Add the record near `CombatantConditions`:

```csharp
public sealed record ConditionTimer(Condition Condition, int? RoundsRemaining = null);
```

Retype the `Conditions` helper on `Combatant`:

```csharp
    [NotMapped]
    public IReadOnlyList<ConditionTimer> Conditions
    {
        get => CombatantConditions.Deserialize(ConditionsJson);
        set => ConditionsJson = CombatantConditions.Serialize(value);
    }
```

Rewrite `CombatantConditions` (both shapes; `JsonDocument`/`JsonSerializer` from `System.Text.Json`, `Enumerable` from `System.Linq`):

```csharp
public static class CombatantConditions
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(IReadOnlyList<ConditionTimer> conditions) =>
        JsonSerializer.Serialize(conditions, Options);

    public static IReadOnlyList<ConditionTimer> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

        var firstKind = JsonValueKind.Undefined;
        foreach (var el in doc.RootElement.EnumerateArray()) { firstKind = el.ValueKind; break; }

        // Legacy shape: a JSON array of enum names → indefinite timers.
        if (firstKind == JsonValueKind.String)
            return (JsonSerializer.Deserialize<List<Condition>>(json, Options) ?? [])
                .Select(c => new ConditionTimer(c, null)).ToList();

        // New shape (or empty): array of { Condition, RoundsRemaining }.
        return JsonSerializer.Deserialize<List<ConditionTimer>>(json, Options) ?? [];
    }
}
```

- [ ] **Step 4: Retype the callers (behavior-preserving).**

`CombatRepository.UpdateCombatantAsync` — change ONLY the param type (body already calls `CombatantConditions.Serialize(conditions)`):

```csharp
    public async Task UpdateCombatantAsync(
        long combatantId, long combatId, long campaignId, long userId,
        int currentHp, int? initiativeRoll, int initiativeModifier, IReadOnlyList<ConditionTimer> conditions)
```

`InitiativeTracker.razor` — its existing condition uses become timer-typed (NO new rounds UI yet — that's Task 3). Update via Serena:
- Active chips: `@foreach (var t in c.Conditions)` → chip text `@t.Condition ✕`, remove via `ToggleConditionAsync(c, t.Condition)`.
- Menu highlight: `c.Conditions.Contains(cond)` → `c.Conditions.Any(x => x.Condition == cond)`.
- `ToggleConditionAsync(Combatant c, Condition cond)` body → toggle on the timer list:
  ```csharp
      var set = c.Conditions.ToList();
      var existing = set.FirstOrDefault(t => t.Condition == cond);
      if (existing is not null) set.Remove(existing);
      else set.Add(new ConditionTimer(cond, null));
      await CombatRepo.UpdateCombatantAsync(c.Id, _combat.Id, CampaignId, UserId,
          c.CurrentHp, c.InitiativeRoll, c.InitiativeModifier, set);
  ```
- The `AdjustHpAsync`/`SetInitiativeAsync` calls that pass `c.Conditions` to `UpdateCombatantAsync` now pass timers unchanged — no edit needed beyond confirming they compile.

- [ ] **Step 5: Run to green + build 0/0** — `dotnet test --filter FullyQualifiedName~CombatantConditionsTests` (3 new) + `dotnet build` (MUST be 0/0 — the retype is complete, nothing left red).

- [ ] **Step 6: Commit**

```bash
git add Features/Combat/Combatant.cs Features/Combat/CombatRepository.cs CompanionUI/Components/InitiativeTracker.razor DndMcpAICsharpFun.Tests/Combat/CombatantConditionsTests.cs
git commit -m "feat(combat): ConditionTimer model + backward-compatible conditions retype (behavior-preserving)"
```

---

### Task 2: Tick + expire timed conditions on round rollover

**Files:** Modify `Features/Combat/CombatRepository.cs` (`AdvanceTurnAsync`); Test `DndMcpAICsharpFun.Tests/Combat/CombatRepositoryCombatantTests.cs`.

- [ ] **Step 1: Write the failing tests** (extend `CombatRepositoryCombatantTests`; `Monster(name,init)` + `SeedCombatAsync` exist):

```csharp
    private async Task SetConditionsAsync(long combatantId, long combatId, long campaignId, long userId,
        params ConditionTimer[] timers)
    {
        var c = (await _repo.GetCombatantsAsync(combatId, campaignId, userId)).Single(x => x.Id == combatantId);
        await _repo.UpdateCombatantAsync(combatantId, combatId, campaignId, userId,
            c.CurrentHp, c.InitiativeRoll, c.InitiativeModifier, timers);
    }

    [Fact]
    public async Task Round_rollover_ticks_and_expires_timed_conditions_for_all_combatants()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        var idA = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("A", 20));
        var idB = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("B", 10));
        await SetConditionsAsync(idA, combatId, campaignId, userId,
            new ConditionTimer(Condition.Paralyzed, 2), new ConditionTimer(Condition.Prone, null));
        await SetConditionsAsync(idB, combatId, campaignId, userId, new ConditionTimer(Condition.Poisoned, 1));

        // 2 combatants: A(current)→B (round 1, no wrap), B→wrap A (round 2, tick).
        await _repo.AdvanceTurnAsync(combatId, campaignId, userId);
        await _repo.AdvanceTurnAsync(combatId, campaignId, userId);

        var combatants = await _repo.GetCombatantsAsync(combatId, campaignId, userId);
        var a = combatants.Single(x => x.Id == idA).Conditions;
        a.Single(t => t.Condition == Condition.Paralyzed).RoundsRemaining.Should().Be(1); // 2 → 1
        a.Should().Contain(t => t.Condition == Condition.Prone && t.RoundsRemaining == null); // untouched
        combatants.Single(x => x.Id == idB).Conditions
            .Should().NotContain(t => t.Condition == Condition.Poisoned); // 1 → 0 → expired
    }

    [Fact]
    public async Task Advancing_within_a_round_does_not_tick_conditions()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        var idA = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("A", 20));
        await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("B", 10));
        await SetConditionsAsync(idA, combatId, campaignId, userId, new ConditionTimer(Condition.Poisoned, 3));

        await _repo.AdvanceTurnAsync(combatId, campaignId, userId); // A → B, round stays 1, no tick

        (await _repo.GetCombatantsAsync(combatId, campaignId, userId)).Single(x => x.Id == idA).Conditions
            .Single(t => t.Condition == Condition.Poisoned).RoundsRemaining.Should().Be(3);
    }
```

- [ ] **Step 2: Run — expect FAIL** (no tick). `dotnet test --filter FullyQualifiedName~CombatRepositoryCombatantTests`.

- [ ] **Step 3: Implement.** In `AdvanceTurnAsync`, the combatants are already loaded TRACKED (`db.Combatants.Where(...).ToListAsync()`). In the round-rollover branch, add the tick before the method's existing single `SaveChangesAsync`:

```csharp
        if (next >= ordered.Count)
        {
            next = 0;
            combat.Round += 1;

            // Round rollover: tick timed conditions on every combatant; expire any reaching 0.
            foreach (var cmb in combatants)
            {
                var timers = cmb.Conditions;
                if (timers.Count == 0) continue;
                var ticked = timers
                    .Select(t => t.RoundsRemaining is int r ? t with { RoundsRemaining = r - 1 } : t)
                    .Where(t => t.RoundsRemaining is null || t.RoundsRemaining > 0)
                    .ToList();
                cmb.Conditions = ticked; // writes ConditionsJson on the tracked entity
            }
        }
```

(Persisted by the existing `await db.SaveChangesAsync();` — one atomic write covering the round/turn move + the ticks.)

- [ ] **Step 4: Run to green + build 0/0** — `dotnet test --filter FullyQualifiedName~CombatRepositoryCombatantTests` (existing + 2 new).

- [ ] **Step 5: Commit**

```bash
git add Features/Combat/CombatRepository.cs DndMcpAICsharpFun.Tests/Combat/CombatRepositoryCombatantTests.cs
git commit -m "feat(combat): tick + expire timed conditions on round rollover"
```

---

### Task 3: InitiativeTracker UI — per-chip rounds field

**Files:** Modify `CompanionUI/Components/InitiativeTracker.razor` (Serena); `wwwroot/app.css`.

- [ ] **Step 1: Active chips gain a rounds field.** Change the active-conditions render so each chip shows the condition, a small rounds input (empty ⇒ ∞), and a remove. Replace the `@foreach (var t in c.Conditions)` chip block:

```razor
                            @foreach (var t in c.Conditions)
                            {
                                <span class="chip cond-chip">
                                    @t.Condition
                                    <input type="number" class="cond-rounds" min="1" placeholder="∞"
                                           value="@t.RoundsRemaining"
                                           @onchange="e => SetConditionRoundsAsync(c, t.Condition, e.Value?.ToString())" />
                                    <button type="button" class="cond-remove"
                                            @onclick="() => ToggleConditionAsync(c, t.Condition)">✕</button>
                                </span>
                            }
```

- [ ] **Step 2: Add `SetConditionRoundsAsync`** in `@code` (empty/≤0 ⇒ indefinite):

```csharp
    private async Task SetConditionRoundsAsync(Combatant c, Condition cond, string? raw)
    {
        if (_combat is null) return;
        int? rounds = int.TryParse(raw, out var n) && n > 0 ? n : null;
        var set = c.Conditions
            .Select(t => t.Condition == cond ? t with { RoundsRemaining = rounds } : t)
            .ToList();
        await CombatRepo.UpdateCombatantAsync(c.Id, _combat.Id, CampaignId, UserId,
            c.CurrentHp, c.InitiativeRoll, c.InitiativeModifier, set);
        await ReloadAsync();
    }
```

- [ ] **Step 3: Style** — in `wwwroot/app.css`, style `.cond-chip` (chip holding its controls), `.cond-rounds` (narrow ~34px mono number field, subtle), `.cond-remove` (small) against the tokens; keep the row compact/no overflow.

- [ ] **Step 4: Build 0/0.** (Controller rebuilds + screenshots.)

- [ ] **Step 5: Commit**

```bash
git add CompanionUI/Components/InitiativeTracker.razor wwwroot/app.css
git commit -m "feat(ui): per-condition rounds field on combatant chips (timed vs indefinite)"
```

---

### Task 4: Verify + review

- [ ] **Step 1** — `dotnet build` 0/0; **full `dotnet test`** suite green. Confirm `git diff --name-only` shows NO `.http`/`.insomnia`/`Migrations/`/schema change; confirm the retype broke no other caller (search `UpdateCombatantAsync(` and `.Conditions`).
- [ ] **Step 2** — Rebuild the dev container; Playwright-screenshot: on a combatant set a condition's rounds to 2 (chip reads "poisoned 2"), set an indefinite condition on another, advance the turn across a **round rollover**, confirm the timed one ticks to 1 (and expires on the next rollover) while the indefinite one stays; desktop + mobile, no overflow.
- [ ] **Step 3** — Final whole-branch review (opus): backward-compat deser (both shapes, empty); tick math (decrement on rollover, expire at 0, indefinite never ticks, all-combatants, within-round-no-tick); the AdvanceTurn tick stays in one `SaveChanges` (atomic); retype ripple fully contained; MODIFIED deltas match; no scope creep (no migration/`.http`/`.insomnia`).

## Self-Review

- **Spec coverage:** duration round-trip + legacy-read (T1), rollover tick/expire + indefinite-untouched + all-combatants + within-round-no-tick (T2), per-chip rounds UI (T3). Both MODIFIED requirements' new scenarios map to tests.
- **Green intermediate builds:** T1 does the whole retype atomically (build 0/0); T2/T3 add features on green.
- **Type consistency:** `ConditionTimer(Condition, int? RoundsRemaining=null)`, `Conditions : IReadOnlyList<ConditionTimer>`, `UpdateCombatantAsync(..., IReadOnlyList<ConditionTimer>)`, and the razor's `t.Condition`/`t.RoundsRemaining` are consistent across T1–T3.
