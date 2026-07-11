# Combat Tie Reorder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Backend tasks are TDD with real-Postgres tests; the UI task is verified by build + Playwright screenshots (controller-run). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Let the DM reorder combatants the sort treats as tied (equal initiative roll, modifier, side) with ▲/▼, by swapping their `AddedOrder`.

**Architecture:** Reuse `AddedOrder` as the mutable tie-break (no migration). A pure `CombatantOrder.AreTied` gates both the repo swap (`MoveCombatantAsync`) and the UI ▲/▼ enable/disable, so a swap is only ever issued where it reorders. The identity-based current turn is unaffected.

**Tech Stack:** .NET 10, EF Core + PostgreSQL, Blazor Server, xunit + Testcontainers.PostgreSql, Playwright.

## Global Constraints

- **Presentational + combat model/logic only** — NO new table/migration, NO DI change, NO new HTTP route/MCP tool; do NOT touch `DndMcpAICsharpFun.http`/`dnd-mcp-api.insomnia.json`. Full `dotnet test` suite MUST stay green.
- **warnings-as-errors** (build 0/0). `dotnet` runs need `dangerouslyDisableSandbox: true`. Docker up for the Postgres Testcontainer.
- **Serena for all `.cs`/`.razor` edits**; built-in Read/Edit forbidden on code files. (After a Serena `replace_symbol_body` on a test class, eyeball `[Fact]`/`[Theory]` attributes survived — they can be dropped, tripping `xUnit1013`.)
- `AddedOrder` is reused as the mutable tie-break — the reorder swaps two combatants' `AddedOrder` (need not stay contiguous/unique). `CurrentTurnCombatantId` is identity-based and MUST NOT change on a reorder.
- Each task builds 0/0 (Tasks 1–2 are additive; Task 3 wires the UI).

## File Structure

- Modify `Features/Combat/CombatantOrder.cs` — add `AreTied`.
- Modify `Features/Combat/CombatRepository.cs` — add `MoveCombatantAsync`.
- Modify `CompanionUI/Components/InitiativeTracker.razor` — ▲/▼ per row + `MoveAsync` handler; `wwwroot/app.css` styling.
- Tests under `DndMcpAICsharpFun.Tests/Combat/`.

---

### Task 1: CombatantOrder.AreTied helper

**Files:** Modify `Features/Combat/CombatantOrder.cs`; Test `DndMcpAICsharpFun.Tests/Combat/CombatantOrderTests.cs` (exists).

**Interfaces:**
- Produces: `static bool CombatantOrder.AreTied(Combatant a, Combatant b)` — equal on `InitiativeRoll` (both null = equal), `InitiativeModifier`, `IsPlayer`.

- [ ] **Step 1: Write the failing tests** (extend `CombatantOrderTests`; a small `Combatant` factory likely exists — reuse it, else new one):

```csharp
    [Fact]
    public void AreTied_true_on_equal_keys_including_both_null_roll()
    {
        var a = new Combatant { InitiativeRoll = null, InitiativeModifier = 2, IsPlayer = false };
        var b = new Combatant { InitiativeRoll = null, InitiativeModifier = 2, IsPlayer = false };
        CombatantOrder.AreTied(a, b).Should().BeTrue();

        var c = new Combatant { InitiativeRoll = 15, InitiativeModifier = 0, IsPlayer = true };
        var d = new Combatant { InitiativeRoll = 15, InitiativeModifier = 0, IsPlayer = true };
        CombatantOrder.AreTied(c, d).Should().BeTrue();
    }

    [Fact]
    public void AreTied_false_when_any_ordering_key_differs()
    {
        var baseC = new Combatant { InitiativeRoll = 15, InitiativeModifier = 2, IsPlayer = false };
        CombatantOrder.AreTied(baseC, new Combatant { InitiativeRoll = 14, InitiativeModifier = 2, IsPlayer = false }).Should().BeFalse();
        CombatantOrder.AreTied(baseC, new Combatant { InitiativeRoll = 15, InitiativeModifier = 3, IsPlayer = false }).Should().BeFalse();
        CombatantOrder.AreTied(baseC, new Combatant { InitiativeRoll = 15, InitiativeModifier = 2, IsPlayer = true }).Should().BeFalse();
    }
```

- [ ] **Step 2: Run — expect FAIL** (`AreTied` missing). `dotnet test --filter FullyQualifiedName~CombatantOrderTests`.

- [ ] **Step 3: Implement** in `Features/Combat/CombatantOrder.cs` (add to the class):

```csharp
    /// <summary>
    /// True when the sort cannot distinguish <paramref name="a"/> and <paramref name="b"/> by anything
    /// above <see cref="Combatant.AddedOrder"/> — equal initiative roll (both unset counts as equal),
    /// modifier, and side. This is exactly the condition under which swapping their AddedOrder reorders
    /// them, so it gates both the manual-reorder swap and the UI's ▲/▼ enable state.
    /// </summary>
    public static bool AreTied(Combatant a, Combatant b) =>
        a.InitiativeRoll == b.InitiativeRoll
        && a.InitiativeModifier == b.InitiativeModifier
        && a.IsPlayer == b.IsPlayer;
```

- [ ] **Step 4: Run to green + build 0/0** — `dotnet test --filter FullyQualifiedName~CombatantOrderTests`.

- [ ] **Step 5: Commit**

```bash
git add Features/Combat/CombatantOrder.cs DndMcpAICsharpFun.Tests/Combat/CombatantOrderTests.cs
git commit -m "feat(combat): CombatantOrder.AreTied (the above-AddedOrder equality that makes a swap reorder)"
```

---

### Task 2: MoveCombatantAsync (tie swap)

**Files:** Modify `Features/Combat/CombatRepository.cs`; Test `DndMcpAICsharpFun.Tests/Combat/CombatRepositoryCombatantTests.cs`.

**Interfaces:**
- Produces: `Task MoveCombatantAsync(long combatantId, long combatId, long campaignId, long userId, bool up)`.

- [ ] **Step 1: Write the failing tests** (extend `CombatRepositoryCombatantTests`; `Monster(name,init)` + `SeedCombatAsync` exist. Two monsters with the SAME init are a tie — same modifier 0, same side). Assert order via `CombatantOrder.Sort` on `GetCombatantsAsync`:

```csharp
    [Fact]
    public async Task Move_up_swaps_two_tied_combatants()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        var idA = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("A", 15));
        var idB = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("B", 15)); // tie: same roll/mod/side
        // Initial order (tie broken by AddedOrder): A then B.
        CombatantOrder.Sort(await _repo.GetCombatantsAsync(combatId, campaignId, userId))
            .Select(c => c.Id).Should().Equal(idA, idB);

        await _repo.MoveCombatantAsync(idB, combatId, campaignId, userId, up: true);

        CombatantOrder.Sort(await _repo.GetCombatantsAsync(combatId, campaignId, userId))
            .Select(c => c.Id).Should().Equal(idB, idA); // swapped
    }

    [Fact]
    public async Task Move_against_a_non_tied_neighbor_is_a_no_op()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        var idHi = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("Hi", 20));
        var idLo = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("Lo", 10));

        await _repo.MoveCombatantAsync(idLo, combatId, campaignId, userId, up: true); // Lo(10) can't pass Hi(20)

        CombatantOrder.Sort(await _repo.GetCombatantsAsync(combatId, campaignId, userId))
            .Select(c => c.Id).Should().Equal(idHi, idLo); // unchanged
    }

    [Fact]
    public async Task Move_on_a_foreign_users_combat_is_a_no_op()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        var idA = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("A", 15));
        var idB = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("B", 15));
        var intruder = await _users.CreateAsync("intruder", "hash");

        await _repo.MoveCombatantAsync(idB, combatId, campaignId, intruder, up: true);

        CombatantOrder.Sort(await _repo.GetCombatantsAsync(combatId, campaignId, userId))
            .Select(c => c.Id).Should().Equal(idA, idB); // unchanged
    }

    [Fact]
    public async Task Reordering_does_not_change_the_current_turn()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        var idA = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("A", 15));
        var idB = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("B", 15));
        await _repo.AdvanceTurnAsync(combatId, campaignId, userId); // current = B (top A → next B)
        (await _repo.GetByIdAsync(combatId, campaignId, userId))!.CurrentTurnCombatantId.Should().Be(idB);

        await _repo.MoveCombatantAsync(idB, combatId, campaignId, userId, up: true);

        (await _repo.GetByIdAsync(combatId, campaignId, userId))!.CurrentTurnCombatantId.Should().Be(idB); // unchanged
    }
```

- [ ] **Step 2: Run — expect FAIL** (`MoveCombatantAsync` missing). `dotnet test --filter FullyQualifiedName~CombatRepositoryCombatantTests`.

- [ ] **Step 3: Implement** — add to `CombatRepository`:

```csharp
    public async Task MoveCombatantAsync(long combatantId, long combatId, long campaignId, long userId, bool up)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var owns = await db.Combats
            .AnyAsync(c => c.Id == combatId && c.CampaignId == campaignId && c.UserId == userId);
        if (!owns) return;

        var combatants = await db.Combatants.Where(x => x.CombatId == combatId).ToListAsync(); // tracked
        var ordered = CombatantOrder.Sort(combatants);
        var idx = -1;
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Id == combatantId) { idx = i; break; }
        }
        if (idx < 0) return;

        var neighborIdx = up ? idx - 1 : idx + 1;
        if (neighborIdx < 0 || neighborIdx >= ordered.Count) return;

        var target = ordered[idx];
        var neighbor = ordered[neighborIdx];
        if (!CombatantOrder.AreTied(target, neighbor)) return; // only genuine ties swap

        (target.AddedOrder, neighbor.AddedOrder) = (neighbor.AddedOrder, target.AddedOrder);
        await db.SaveChangesAsync(); // one atomic write on the two tracked entities
    }
```

- [ ] **Step 4: Run to green + build 0/0** — `dotnet test --filter FullyQualifiedName~CombatRepositoryCombatantTests` (existing + 4 new).

- [ ] **Step 5: Commit**

```bash
git add Features/Combat/CombatRepository.cs DndMcpAICsharpFun.Tests/Combat/CombatRepositoryCombatantTests.cs
git commit -m "feat(combat): MoveCombatantAsync swaps two tied combatants' AddedOrder (ownership-scoped, atomic)"
```

---

### Task 3: InitiativeTracker UI — ▲/▼ per row

**Files:** Modify `CompanionUI/Components/InitiativeTracker.razor` (Serena); `wwwroot/app.css`.

- [ ] **Step 1: Add ▲/▼, enabled by AreTied.** The row loop is `@foreach (var c in ordered)`. Add an index counter and per-row `canUp`/`canDown`, and a `.c-reorder` control at the START of the `<li>`. Change the loop to:

```razor
                @{ var ordered = CombatantOrder.Sort(_combatants); var currentTurnId = _combat.CurrentTurnCombatantId ?? ordered.FirstOrDefault()?.Id; var idx = 0; }
                @foreach (var c in ordered)
                {
                    var isCurrent = c.Id == currentTurnId;
                    var canUp = idx > 0 && CombatantOrder.AreTied(c, ordered[idx - 1]);
                    var canDown = idx < ordered.Count - 1 && CombatantOrder.AreTied(c, ordered[idx + 1]);
                    <li class="@(isCurrent ? "combatant current" : "combatant")">
                        <span class="c-reorder">
                            <button type="button" class="reorder-btn" disabled="@(!canUp)"
                                    title="Move up (tie)" @onclick="() => MoveAsync(c, true)">▲</button>
                            <button type="button" class="reorder-btn" disabled="@(!canDown)"
                                    title="Move down (tie)" @onclick="() => MoveAsync(c, false)">▼</button>
                        </span>
                        <span class="c-init">
                            <input type="number" value="@c.InitiativeRoll"
                                   @onchange="e => SetInitiativeAsync(c, e.Value?.ToString())" />
                        </span>
                        @* …existing c-name / c-hp / c-conditions / remove unchanged… *@
```

Add `idx++;` at the END of the `@foreach` body (after the closing `</li>`). Keep the rest of the row exactly as-is.

- [ ] **Step 2: Add the handler** in `@code`:

```csharp
    private async Task MoveAsync(Combatant c, bool up)
    {
        if (_combat is null) return;
        await CombatRepo.MoveCombatantAsync(c.Id, _combat.Id, CampaignId, UserId, up);
        await ReloadAsync();
    }
```

- [ ] **Step 3: Style** — in `wwwroot/app.css`, style `.c-reorder` (a tight vertical stack of the two arrows) and `.reorder-btn` (small, token colors, obvious `:disabled` state — muted/low-opacity) against the design system; keep the row compact, no overflow.

- [ ] **Step 4: Build 0/0.** (Controller rebuilds + screenshots.)

- [ ] **Step 5: Commit**

```bash
git add CompanionUI/Components/InitiativeTracker.razor wwwroot/app.css
git commit -m "feat(ui): ▲/▼ tie-reorder controls on combatant rows (enabled only for genuine ties)"
```

---

### Task 4: Verify + review

- [ ] **Step 1** — `dotnet build` 0/0; **full `dotnet test`** suite green. Confirm `git diff --name-only` shows NO `.http`/`.insomnia`/`Migrations/`/schema change.
- [ ] **Step 2** — Rebuild the dev container; Playwright-screenshot: add two combatants with the SAME initiative (a tie), use ▲/▼ to swap their order; confirm the arrows are disabled on a unique-initiative combatant, and the current-turn highlight stays on the same combatant across a reorder; desktop + mobile, no overflow.
- [ ] **Step 3** — Final whole-branch review (opus): `AreTied` matches the sort's above-`AddedOrder` keys; `MoveCombatantAsync` swaps only genuine ties, ownership-scoped, atomic (single SaveChanges), current-turn preserved, edge/non-tie no-ops; the UI enable/disable uses the same helper (no swap issued where it wouldn't reorder); MODIFIED delta preserves all prior sort/advance/round-tick/remove-current content; no scope creep.

## Self-Review

- **Spec coverage:** `AreTied` (T1), tie swap + non-tie no-op + ownership + current-turn-preserved (T2), UI ▲/▼ enabled-by-tie (T3). Both new MODIFIED scenarios (reorder swaps a tie; non-tie is a no-op) map to tests.
- **Green intermediate builds:** T1/T2 additive; T3 wires UI. Each builds 0/0.
- **Type consistency:** `AreTied(Combatant, Combatant)` and `MoveCombatantAsync(long, long, long, long, bool)` are consistent across T1–T3 and the razor `MoveAsync` call.
