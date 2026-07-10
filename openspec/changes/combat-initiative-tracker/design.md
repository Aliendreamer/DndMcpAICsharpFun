## Context

The companion already ships the table-play building blocks — `Features/Dice` (deterministic
`DiceRoller` over an injected `IRandomSource`), `Features/Encounters` (`EncounterDesignService`
producing a built encounter with monster refs), and `campaign-log-history` (`CampaignLogEntry` +
`CampaignLogRepository`) — but they are all embedded on `CampaignDetail.razor` and there is no way to
run an actual fight. Hero HP lives in an **append-only** `HeroSnapshot` history (`SaveSnapshotAsync`
adds a new row; the `CharacterSheet` is a JSON blob with `CurrentHitPoints`). Persistence is one EF
Core `AppDbContext` on PostgreSQL; repositories use `IDbContextFactory` for short-lived Blazor-safe
contexts and scope every command by the owning `UserId`. Blazor Server circuits drop on any network
blip or idle timeout, so any live-fight state held only in component memory is lost on reconnect.

## Goals / Non-Goals

**Goals:**

- A persisted combat/initiative tracker that survives reload and circuit drops.
- A focused per-campaign play page hosting all table-play tools, decluttering `CampaignDetail`.
- Draft combatants from the party, a built encounter, and manual add; track initiative order,
  round, per-combatant HP and conditions.
- DM-approval-gated write-back of post-fight HP to hero snapshots — no silent sheet mutation.
- A combat breadcrumb in the existing campaign log when a fight ends.

**Non-Goals:**

- Monster "swarm" quantities / "N goblins" as one line (that is encounter-design v2, its own spec).
- Simulating condition *mechanics* (advantage, speed 0, exhaustion penalties) — conditions are
  tracked as status labels only.
- Pulling monster Dex modifiers from the entity store for initiative (deferred; keeps the tracker
  free of a Qdrant dependency — default modifier 0, DM-editable).
- Any HTTP or MCP surface — server-side Blazor calls the repository/services directly.
- A global (non-campaign) scratch play surface — the play page is per-campaign.

## Decisions

**Two-table relational model over a JSON blob.** `Combat` is a parent aggregate whose children
(`Combatant`) are mutated constantly during a live fight (HP ticks, condition toggles, turn marker).
Per-row updates via `UpdateCombatantAsync` are cheaper and less lost-update-prone than rewriting a
whole `CombatantsJson` blob on every change, and a real `Combatant` row gives queryable history and a
clean `HeroId` FK for write-back. *Alternative considered:* a single `Combat` row with a
`CombatantsJson` column (mirroring `CampaignLogEntry.PayloadJson` / `HeroSnapshot.Sheet`) — simpler
schema but coarse writes and no per-combatant queryability. Rejected for the mutation profile.
*Also rejected:* reusing `CampaignLogEntry` for combat — the log is append-only timeline; combat is a
mutable live aggregate.

**Conditions as a fixed 15-value enum, edition-independent.** The D&D condition list is stable
core-rules content; the 2024 revision changed Exhaustion's mechanics but not the *set*. A hard-coded
enum keeps the tracker dependency-free (no Qdrant query to mark someone "poisoned") and always
available. Stored per combatant as a JSON array of enum names in a `ConditionsJson` column (avoids a
third join table for a small fixed set). *Alternative considered:* loading `Type=Condition` records
from `dnd_entities` — literal to "what's in the database" but adds a Qdrant dependency, latency, and
depends on Conditions being ingested per book. Rejected.

**Split initiative entry: players manual, monsters auto-rolled.** Matches real table play — players
roll their own physical dice and announce a number (DM types it; `InitiativeRoll` starts null), while
a pile of monsters is tedious to roll by hand so they auto-roll `d20 + InitiativeModifier` via the
shipped `DiceRoller` on add. Every value stays hand-editable; the list re-sorts on change. Auto-roll
uses the injected `IRandomSource`, so it is deterministic under test with a seeded source.

**DM-approval-gated HP write-back via a new append-only snapshot.** Ending a combat opens a review of
each player combatant's before→after HP (editable); nothing touches a hero sheet until the DM
approves. On approval, per linked hero, `CombatService` clones the latest sheet, sets
`CurrentHitPoints`, and calls `SaveSnapshotAsync` (label "Post-combat: {name}") — honest, auditable,
non-destructive (old snapshots preserved). *Alternative considered:* self-contained combat HP with no
write-back — simpler but the user explicitly wants the campaign to reflect post-fight HP with no
cheating. *Also considered:* mutating the latest snapshot in place — rejected; snapshots are
append-only history.

**One active combat per campaign; ended combats retained.** `StartAsync` rejects a second active
combat, keeping the tracker unambiguous. Ended combats stay as full records (`GetHistoryAsync`) *and*
drop a `Combat`-kind `CampaignLogEntry` breadcrumb — the "(c) both" history model.

**Relocate existing panels to the play page.** `CampaignDetail` is already crowded (roster,
add-hero, dice, encounter, log, notes). Moving the self-contained dice/encounter/log components onto
`/campaigns/{id}/table` declutters both surfaces and gives the tracker its party+monster context.
This is a low-risk move (the components just take `CampaignId`/`UserId`) but it *does* change the
render-location requirements of `dice-roller` and `campaign-log-history` — captured as MODIFIED
deltas.

**Cross-aggregate work lives in `CombatService`, not the repository.** Drafting the party (reads
`HeroRepository`), auto-rolling monster initiative (uses `DiceRoller`), and end-combat orchestration
(writes `HeroSnapshot` + `CampaignLogEntry`) span multiple aggregates. `CombatRepository` stays a
thin ownership-scoped data gateway; `CombatService` composes it with the other repos/services.
`AddCombat` registers both and pulls in its dependencies so the consumer `Add*` chain is
self-contained (the DI-gate rule).

## Risks / Trade-offs

- **[Migration drifts and touches other tables]** → Verify the generated `Up()` creates ONLY
  `Combats` + `Combatants` + their indexes, `Down()` drops them, and the model-snapshot diff is a
  pure insertion; investigate before committing if anything else is touched.
- **[Orphaned combats on campaign delete]** → `Combat`→`Campaign` cascade *and* an explicit
  `Combats` `ExecuteDeleteAsync` line added to `CampaignRepository.DeleteAsync` inside its existing
  execution-strategy transaction (parent-scoped-table rule).
- **[Blazor render loop crash on partial combat data]** → Null-guard combatant lists and catch
  broadly (`Exception`, not just `JsonException`) around any payload rendering; the render must
  degrade, not throw out of the circuit.
- **[Lost fight on circuit drop]** → State is persisted; the tracker rehydrates the active combat
  from `GetActiveAsync` on load. This is the core reason persisted was chosen over ephemeral.
- **[Write-back races with a mid-session level-up]** → Write-back only sets `CurrentHitPoints` on a
  clone of the *current* latest sheet at approval time, so it composes with whatever the sheet is
  then; the DM reviews the numbers before approving.
- **[Monster initiative modifier is 0 by default]** → Accepted; DM-editable + re-roll. Pulling Dex
  from the monster entity is a deferred enhancement, out of scope to avoid a Qdrant dependency.

## Migration Plan

1. Add `Combat`/`Combatant` entities, `Condition` enum, and the `Combat` payload record.
2. Add two `DbSet`s + entity configuration (cascades, indexes) to `AppDbContext`.
3. Generate the EF migration with the existing design-time factory; verify additive-only.
4. Add the `Combats` cascade line to `CampaignRepository.DeleteAsync`.
5. Build repository + service with real-Postgres Testcontainer tests.
6. Build the tracker component + play page; relocate the three panels off `CampaignDetail`.
7. Wire DI; run the full suite (incl. `FullContainerScopeValidationTests`) at final verify.

Rollback: the migration is additive (two new tables); dropping them and reverting the code removes
the feature with no impact on existing tables. No data backfill is required.
