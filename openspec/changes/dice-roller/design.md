## Context

The Blazor UI is Blazor Server interactive (`AddInteractiveServerRenderMode`), with pages under
`CompanionUI/Pages/` (incl. `Campaigns/CampaignDetail.razor`) and shared components under
`CompanionUI/Components/`. There is no existing dice/RNG utility. The app already renders
campaign-scoped pages the signed-in user owns. This slice (Item A) is UI + a small pure core; it
deliberately does NOT persist rolls — that is Item B (a campaign roll/encounter history).

## Goals / Non-Goals

**Goals:**

- A pure, fully-tested dice core: parse standard `NdX±K` (+ adv/dis) notation and roll it, with the
  RNG as the single injected nondeterminism so every rule is deterministically testable.
- A reusable roller component embedded on the campaign page, showing the result + a readable
  breakdown + a session-local recent-rolls list.

**Non-Goals:**

- Persistence / history / reveal (Item B).
- Advanced dice notation — keep/drop (`4d6kh3`), multiple terms (`2d6+1d4`), exploding, rerolls
  (a possible v2; out of scope here).
- A dedicated page, global widget, HTTP endpoint, or MCP tool — the component embeds on
  `CampaignDetail` and calls the in-process core.

## Decisions

### Pure core, RNG as the only injected nondeterminism

`DiceExpression` (parse/validate), `DiceRoller.Roll` (apply RNG), and `RollResult` (values +
breakdown) live in `Features/Dice/` and are pure except for `IRandomSource`. Tests inject a
scripted/seeded `IRandomSource` and assert exact die values, kept dice, totals, and breakdown
strings — no flakiness. Production registers a `Random`-backed `IRandomSource`. *Alternative
considered:* rolling inside the component with `Random` directly — rejected; that couples the rules
to the UI and makes them untestable without bUnit.

### Advantage/disadvantage is d20-only and rolls two, keeps one

`DiceExpression` carries an adv/dis flag valid only when the die is d20 and count is 1 (parser
rejects otherwise). `Roll` rolls two d20; advantage keeps the higher, disadvantage the lower; both
kept dice are reported so the breakdown can show `[18,7] → 18`. *Alternative considered:* generic
keep-highest/lowest-N — deferred to the advanced-notation v2.

### Server-side roll in the Blazor circuit

The roll executes server-side (the component calls the injected `DiceRoller`), consistent with the
app's Blazor Server model and with Item B (which will persist server-side). No client JS RNG.
Latency is a non-issue for a single click.

### Component reusable, embedded on CampaignDetail; rolls ephemeral

The `DiceRoller` component is self-contained (takes no campaign dependency in Item A) and is
embedded on `CampaignDetail.razor`. It keeps a session-local list of the last ~10 rolls in component
state (cleared on navigation/refresh). Item B will add an optional callback/parameter so the same
component can also persist a roll to the campaign history — nothing in Item A blocks that.

## Risks / Trade-offs

- **Parser ambiguity / bad input** → the parser returns a clear validation error (surfaced in the
  component as a message), never an exception bubbling to the circuit. *Mitigation:* explicit
  `TryParse`-style result; tests cover invalid inputs (bad die, adv/dis on non-d20, huge counts).
- **Absurd counts (e.g. `999d100`)** → cap the count at a sane maximum (e.g. 100 dice) in the
  parser and reject above it, so a roll can't allocate/format unboundedly.
- **RNG quality** → `System.Random` is fine for a table companion (not security-sensitive); the
  seam allows swapping later if needed.
- **Component test depth** → without bUnit, the component is build-verified + a light manual/
  Playwright check; correctness lives in the pure-core unit tests, which is where the rules are.
