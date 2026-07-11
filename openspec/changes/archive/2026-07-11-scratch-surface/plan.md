# Scratch Surface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a global `/scratch` page (reached from a new "🎲 Scratchpad" sidebar entry) for off-campaign ephemeral dice rolling and explicit-party encounter building.

**Architecture:** Pure Blazor Server wiring — no new domain, service, persistence, migration, HTTP route, or MCP tool. Reuse the shipped `DiceRollerPanel` at `CampaignId=0` (already ephemeral — its auto-log is guarded by `if (CampaignId > 0)`) and the shipped `EncounterDesignService.BuildForUserAsync` explicit-`partyLevels` path (already tested; when `partyLevels` has `Count > 0` it wins and never touches a campaign). `EncounterPanel` gains one optional parameter and one render guard so it can build for an explicit party and hide the "Save to log" row off-campaign — both no-ops for the existing campaign table page.

**Tech Stack:** .NET 10, ASP.NET Core, Blazor Server (`@rendermode InteractiveServer`), single host on :5101.

## Global Constraints

- Serena symbolic tools only for reading/editing code — built-in Read/Edit on code files is forbidden (project rule).
- Warnings-as-errors for every project (`Directory.Build.props`) — build must be 0 warnings / 0 errors.
- No new domain/persistence/migration, no DI change, no HTTP route or MCP tool → **no `DndMcpAICsharpFun.http` / `dnd-mcp-api.insomnia.json` change** (nothing to sync).
- The `EncounterPanel` changes MUST be behavior-neutral for the campaign table page (`CampaignTable.razor`): with `PartyLevels` unset and `CampaignId > 0` it still resolves the party from the campaign and still shows "Save to log".
- No new unit tests (reuses already-tested services). Verification per task = `dotnet build` 0/0 + full `dotnet test` green. Visual behavior is verified once at the end via Playwright (see "Visual Verification").
- Auth pattern for pages: `@attribute [Authorize]`, `_userId = long.Parse(state.User.FindFirst(ClaimTypes.NameIdentifier)!.Value)` — copy verbatim from `CampaignTable.razor`.

---

## File Structure

- **Modify** `CompanionUI/Components/EncounterPanel.razor` — add optional `[Parameter] IReadOnlyList<int>? PartyLevels`; pass it to `BuildForUserAsync` (instead of hard-coded `null`); wrap the `encounter-save-row` `<div>` in `@if (CampaignId > 0)`.
- **Create** `CompanionUI/Pages/Scratch.razor` — the `/scratch` page: `DiceRollerPanel CampaignId=0`, a party size/level input, and `EncounterPanel CampaignId=0 PartyLevels=…`.
- **Modify** `CompanionUI/Layout/MainLayout.razor` — add the "🎲 Scratchpad" `NavLink` → `/scratch`.

---

### Task 1: EncounterPanel — optional explicit party + off-campaign save guard

**Files:**
- Modify: `CompanionUI/Components/EncounterPanel.razor`

**Interfaces:**
- Consumes: `EncounterDesignService.BuildForUserAsync(long userId, long? campaignId, IReadOnlyList<int>? partyLevels, Difficulty target, DndVersion ed, string? theme, double? crLte, double? crGte, CancellationToken ct)` — already exists; the `partyLevels` argument is currently passed as `null`.
- Produces: a new `[Parameter] public IReadOnlyList<int>? PartyLevels { get; set; }` on `EncounterPanel`, consumed by Task 2's page.

**Context:** `EncounterPanel.razor`'s `BuildAsync()` calls `BuildForUserAsync(UserId, CampaignId, partyLevels: null, …)`. `ResolvePartyAsync` returns an explicit `partyLevels` verbatim when `partyLevels is { Count: > 0 }` — so a non-empty `PartyLevels` never reaches the campaign lookup. The save UI is the `<div class="encounter-save-row">…</div>` inside the `_built is not null` block. There are no tests referencing `EncounterPanel`, so the guard against regression is the full suite staying green (it exercises the encounter service/campaign flows) plus build 0/0.

- [ ] **Step 1: Add the `PartyLevels` parameter**

In the `@code` block, add alongside the other parameters:

```csharp
    [Parameter] public IReadOnlyList<int>? PartyLevels { get; set; }
```

(place it after `[Parameter] public EventCallback<BuiltEncounter> OnBuilt { get; set; }`)

- [ ] **Step 2: Pass `PartyLevels` to the service**

In `BuildAsync()`, change the `partyLevels:` argument from `null` to the parameter:

```csharp
            _built = await EncounterSvc.BuildForUserAsync(
                UserId,
                CampaignId,
                partyLevels: PartyLevels,
                _difficulty,
                _edition,
                string.IsNullOrWhiteSpace(_theme) ? null : _theme,
                crLte: null,
                crGte: null,
                default);
```

When `PartyLevels` is `null` (campaign table page), behavior is identical to today.

- [ ] **Step 3: Guard the save row off-campaign**

Wrap the save row so it only renders when there is a campaign to save to. Change:

```razor
            <div class="encounter-save-row">
                <input type="text" @bind="_label" @bind:event="oninput" placeholder="Label (optional)" />
                <label><input type="checkbox" @bind="_hidden" /> Hidden</label>
                <button type="button" class="btn btn--primary" @onclick="SaveAsync">Save to log</button>
            </div>
```

to:

```razor
            @if (CampaignId > 0)
            {
                <div class="encounter-save-row">
                    <input type="text" @bind="_label" @bind:event="oninput" placeholder="Label (optional)" />
                    <label><input type="checkbox" @bind="_hidden" /> Hidden</label>
                    <button type="button" class="btn btn--primary" @onclick="SaveAsync">Save to log</button>
                </div>
            }
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Run the full suite**

Run: `dotnet test` (Docker must be running for the Postgres Testcontainer persistence tests)
Expected: all tests pass — the campaign encounter flow is unchanged (`PartyLevels` unset, `CampaignId > 0`).

- [ ] **Step 6: Commit**

```bash
git add CompanionUI/Components/EncounterPanel.razor
git commit -m "feat(scratch-surface): EncounterPanel optional explicit party + off-campaign save guard"
```

---

### Task 2: Scratch page + Scratchpad nav entry

**Files:**
- Create: `CompanionUI/Pages/Scratch.razor`
- Modify: `CompanionUI/Layout/MainLayout.razor`

**Interfaces:**
- Consumes: `DiceRollerPanel` (`[Parameter] long CampaignId`, `[Parameter] long UserId`, `[Parameter] EventCallback OnLogged` — omit `OnLogged` off-campaign); `EncounterPanel` (`CampaignId`, `UserId`, `PartyLevels` from Task 1); the `NameIdentifier` claim auth pattern from `CampaignTable.razor`.
- Produces: the `/scratch` route and the sidebar link to it.

**Context:** `CampaignTable.razor` is the template for an authorized `InteractiveServer` page that hosts `DiceRollerPanel` and `EncounterPanel`. The scratch page differs in three ways: no `Id` route param, `CampaignId="0"` on both panels, and a party size/level input that produces `PartyLevels`. Defaulting size to 4 and level to 1 (and clamping size to a 1–10 min/max and level to 1–20) guarantees `_partyLevels` is always non-empty, so `EncounterPanel`'s own Build button can never hit the "empty party" `ArgumentException` from `ResolvePartyAsync`. `System.Linq` is a global implicit using, so `Enumerable.Repeat` needs no extra `@using`.

- [ ] **Step 1: Create the scratch page**

Create `CompanionUI/Pages/Scratch.razor`:

```razor
@page "/scratch"
@rendermode InteractiveServer
@using Microsoft.AspNetCore.Authorization
@using System.Security.Claims
@attribute [Authorize]
@inject AuthenticationStateProvider Auth

<PageTitle>Scratchpad</PageTitle>

<div class="scratch-page">
    <h1>🎲 Scratchpad</h1>
    <p class="muted">Quick dice and encounter math — nothing here is saved to a campaign.</p>

    <DiceRollerPanel CampaignId="0" UserId="_userId" />

    <div class="scratch-party">
        <label>
            Party size
            <input type="number" min="1" max="10" @bind="_partySize" @bind:event="oninput" />
        </label>
        <label>
            Level
            <input type="number" min="1" max="20" @bind="_partyLevel" @bind:event="oninput" />
        </label>
    </div>

    <EncounterPanel CampaignId="0" UserId="_userId" PartyLevels="_partyLevels" />
</div>

@code {
    private long _userId;
    private int _partySize = 4;
    private int _partyLevel = 1;

    private IReadOnlyList<int> _partyLevels =>
        Enumerable.Repeat(Math.Clamp(_partyLevel, 1, 20), Math.Clamp(_partySize, 1, 10)).ToList();

    protected override async Task OnInitializedAsync()
    {
        var state = await Auth.GetAuthenticationStateAsync();
        _userId = long.Parse(state.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    }
}
```

- [ ] **Step 2: Add the sidebar nav entry**

In `CompanionUI/Layout/MainLayout.razor`, inside `<div class="sidebar-nav">`, add a fourth link after the Heroes `NavLink`:

```razor
            <NavLink href="/heroes" ActiveClass="active">🦸 Heroes</NavLink>
            <NavLink href="/scratch" ActiveClass="active">🎲 Scratchpad</NavLink>
```

(`NavLink` without `Match="NavLinkMatch.All"` uses prefix matching, which highlights `/scratch` on that route and not on `/` — same pattern as Campaigns and Heroes.)

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: all tests pass (no behavior change to tested code).

- [ ] **Step 5: Commit**

```bash
git add CompanionUI/Pages/Scratch.razor CompanionUI/Layout/MainLayout.razor
git commit -m "feat(scratch-surface): /scratch page + Scratchpad sidebar entry"
```

---

## Visual Verification (controller-run, after both tasks)

Not a subagent task — the controller runs these with Playwright against a live `dotnet run` (:5101), logging in as the seeded `test` user, and captures screenshots. This is the real acceptance gate for a presentational change (build + suite are green by construction here).

- [ ] Navigate to `/scratch` (desktop + mobile viewport): the dice roller and encounter builder render; the "🎲 Scratchpad" sidebar link shows as active; the page body does not scroll horizontally on mobile (`browser_evaluate` `document.documentElement.scrollWidth <= window.innerWidth`).
- [ ] Roll dice on `/scratch`: the result and breakdown appear in the session list; confirm no campaign-log write occurred (nothing to refresh — `DiceRollerPanel` at `CampaignId=0` skips `AddRollAsync`).
- [ ] Set party size + level and click **Build**: difficulty, XP, and monsters render, and there is **no** "Save to log" row.
- [ ] Open a campaign table page (`/campaigns/{id}/table`) and confirm the encounter builder is unchanged — it still shows "Save to log" and still saves (regression check for the Task 1 change).

---

## Self-Review

**Spec coverage:**
- `scratch-surface` §"Global scratch page … reachable and campaign-free" → Task 2 (page at `/scratch`, dice + encounter only, no campaign) + Visual Verification.
- `scratch-surface` §"Scratch dice rolls are ephemeral" → Task 2 (`DiceRollerPanel CampaignId="0"`, its `CampaignId > 0` guard) + Visual Verification (no log write).
- `scratch-surface` §"Scratch encounters build against a typed party" (build from size/level; no save affordance off-campaign) → Task 1 (`PartyLevels` param + `@if (CampaignId > 0)` save guard) + Task 2 (`Enumerable.Repeat(level, size)`) + Visual Verification.
- `sidebar-navigation` MODIFIED §"Sidebar highlights the active route" (+ Scratchpad-active scenario) → Task 2 Step 2 (the `NavLink`) + Visual Verification (active on `/scratch`).

**Placeholder scan:** none — every code step shows the full razor/C#.

**Type consistency:** `IReadOnlyList<int>? PartyLevels` (Task 1) matches `BuildForUserAsync`'s `IReadOnlyList<int>? partyLevels` parameter and the `_partyLevels` computed property's `IReadOnlyList<int>` return (Task 2). `CampaignId`/`UserId` are `long` on both panels. Auth line copied verbatim from `CampaignTable.razor`.
