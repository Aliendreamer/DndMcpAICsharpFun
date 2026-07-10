## Why

The companion has table-play building blocks — a dice roller, an encounter builder, and a
campaign log — but they are all crammed onto the campaign detail page and there is no way to
actually **run a fight**. A DM at the table needs a persisted initiative tracker (turn order,
round counter, per-combatant HP and conditions) that survives a reload or a dropped Blazor
circuit, and a focused surface to run a session from instead of the crowded management page.

## What Changes

- **New per-campaign play page** at `/campaigns/{id}/table` that hosts the table-play tools
  together (dice roller, encounter builder, campaign log, and the new initiative tracker).
- **Relocate** the dice roller, encounter panel, and campaign log **off** `CampaignDetail.razor`
  onto the play page; `CampaignDetail` keeps the roster + notes and gains a **"▶ Run session"**
  link. Sidebar navigation is unchanged (the play page is reached from the campaign).
- **New persisted combat / initiative tracker**: a two-table relational model (`Combat` +
  `Combatant`) with an additive EF migration. One active combat per campaign; full ended-combat
  history. Combatants carry initiative, HP, AC, an `IsPlayer` flag, and a multi-select set of
  D&D conditions (a fixed 15-value enum, edition-independent).
- **Drafting combatants** from three sources: the campaign's party heroes (HP/AC from the latest
  sheet, manual initiative entry), a built encounter's monsters (auto-rolled initiative via the
  shipped `DiceRoller`), and manual ad-hoc add.
- **DM-approved HP write-back**: ending a combat opens a review panel; only on approval does each
  linked hero get a **new append-only `HeroSnapshot`** with its post-fight HP. Nothing touches
  hero sheets without confirmation.
- **Campaign-log breadcrumb**: ending a combat drops a `Combat`-kind `CampaignLogEntry` into the
  existing timeline.

No new HTTP route and no MCP tool — server-side Blazor components call the repository and services
directly, so there is no `.http` / `.insomnia` change.

## Capabilities

### New Capabilities

- `combat-initiative-tracker`: the per-campaign play page, the persisted `Combat`/`Combatant`
  model + repository + orchestration service, combatant drafting (party / encounter / manual),
  initiative ordering and turn/round advancement, condition tracking, and DM-approval-gated HP
  write-back to hero snapshots.

### Modified Capabilities

- `dice-roller`: the dice roller component is now embedded on the campaign **play (table) page**
  rather than the campaign detail page (render-location change only; roll behavior unchanged).
- `campaign-log-history`: the log timeline, the encounter-save panel, and the auto-logged rolls now
  render on the campaign **play (table) page**; the unified `CampaignLogEntry.Kind` gains a
  **`Combat`** value with a combat-summary payload, dropped as a breadcrumb when a combat ends.

## Impact

- **New code**: `Features/Combat/` (`Combat`, `Combatant` entities, `Condition` enum,
  `CombatRepository`, `CombatService`), `CompanionUI/Pages/Campaigns/CampaignTable.razor`,
  `CompanionUI/Components/InitiativeTracker.razor`.
- **Modified code**: `CampaignDetail.razor` (remove the three panels, add the play link),
  `AppDbContext` (two `DbSet`s + entity config + additive migration + snapshot),
  `CampaignRepository.DeleteAsync` (add a `Combats` cascade line — parent-scoped-table rule),
  `CampaignLogPayloads` (a combat payload record), DI registration (`AddCombat`, wired into the
  consumer `Add*` chain).
- **Dependencies**: reuses shipped `DiceRoller` + `IRandomSource`, `HeroRepository`,
  `CampaignLogRepository`, and `EncounterDesignService`'s built-encounter output. No new packages.
- **Persistence**: two new PostgreSQL tables (`Combats`, `Combatants`) with cascade deletes and
  ownership indexes; covered by the real-Postgres Testcontainer suite.
