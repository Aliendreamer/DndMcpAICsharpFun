## Why

Companion table-play (Item A). A D&D companion used during play needs to roll dice — the single
most common table action. There is no roller today. It's a self-contained slice with a crisp,
testable core (dice notation + RNG), and it sets up Item B (persisting rolls into a campaign
history/timeline) by living on the campaign page as a reusable component.

## What Changes

- Add a pure, testable dice core in `Features/Dice/` (the only nondeterminism is an injected RNG):
  - **`DiceExpression`** — parses standard notation `NdX±K`: optional count `N` (default 1), die
    `X ∈ {4,6,8,10,12,20,100}`, optional `+K`/`-K` modifier, plus an **advantage/disadvantage** flag
    (d20 only). Rejects malformed input with a clear message (bad die size, unparseable,
    adv/dis on a non-d20, absurd counts).
  - **`IRandomSource`** — a seedable RNG seam: production is `Random`-backed; tests use a
    scripted/seeded source so rolls are exact and assertable.
  - **`DiceRoller.Roll(DiceExpression) → RollResult`** — rolls each die via `IRandomSource`;
    advantage rolls two d20 and keeps the highest, disadvantage keeps the lowest.
  - **`RollResult`** — the individual die values, the kept dice (for adv/dis), the modifier, the
    total, and a human-readable breakdown (`"2d6+3 → [4,5]+3 = 12"`, `"d20 (adv) → [18,7] → 18"`).
- Add a reusable **`DiceRoller` Blazor component** (`CompanionUI/Components/`) embedded in
  `CompanionUI/Pages/Campaigns/CampaignDetail.razor`: quick-die buttons (d4…d100) + count stepper +
  `+/-` modifier + adv/dis toggle (enabled for d20) or a free-text expression field; a **Roll**
  button that invokes the in-process core server-side (Blazor Server interactive) and shows the
  result + breakdown; and a **session-local "recent rolls" list** (last ~10 this session, in
  component memory) — **ephemeral, no DB**.
- **No DB, no new HTTP route** (a server-side interactive component calling the in-process core) →
  no `.http`/`.insomnia` change.

## Capabilities

### New Capabilities

- `dice-roller`: the `DiceExpression` parser, the `IRandomSource` seam, `DiceRoller`/`RollResult`,
  and the reusable Blazor `DiceRoller` component embedded on the campaign page (ephemeral rolls).

### Modified Capabilities

- (none — the component is embedded on `CampaignDetail` as an additive UI element; no existing
  capability's requirements change.)

## Impact

- **Code:** new `Features/Dice/` (core) + `CompanionUI/Components/DiceRoller.razor` (component),
  embedded on `CampaignDetail.razor`; `IRandomSource` DI-registered.
- **APIs:** none — no HTTP route, no MCP tool, no auth-surface change. The roll runs server-side in
  the Blazor circuit.
- **Data:** none — rolls are ephemeral (session/in-component only). Persistence is Item B.
- **Docs:** none (`.http`/`.insomnia` unchanged).
