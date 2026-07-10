## 1. Domain + data model (`Features/Combat/`)

- [ ] 1.1 Add the `Condition` enum (fixed 15 values, edition-independent) under `Features/Combat/`
- [ ] 1.2 Add the `Combat` entity (Id, CampaignId, UserId, Name, Edition, Status, Round, CurrentTurnIndex, CreatedAt, EndedAt?) and a `CombatStatus` enum (Active|Ended)
- [ ] 1.3 Add the `Combatant` entity (Id, CombatId, HeroId?, Name, IsPlayer, InitiativeRoll?, InitiativeModifier, MaxHp, CurrentHp, Ac?, ConditionsJson, AddedOrder)
- [ ] 1.4 Add a `CombatLogPayload` record to `CampaignLogPayloads` (combat name, edition, round count, combatant summary) and extend `CampaignLogKind` with `Combat`

## 2. EF wiring + migration

- [ ] 2.1 Add `DbSet<Combat>` + `DbSet<Combatant>` to `AppDbContext` with entity config: `Combatant`→`Combat` cascade, `Combat`→`Campaign` cascade, indexes `IX_Combats_CampaignId_UserId_Status` and `IX_Combatants_CombatId`, `ConditionsJson` stored as a text/JSON column
- [ ] 2.2 Generate the EF migration via the existing design-time factory; VERIFY additive-only (Up() creates ONLY the two tables + indexes, Down() drops them, model-snapshot diff is a pure insertion touching no other entity)
- [ ] 2.3 Add a `Combats` cascade `ExecuteDeleteAsync` line to `CampaignRepository.DeleteAsync` inside its existing execution-strategy transaction; confirm delete order avoids FK violations

## 3. Repository (`CombatRepository`)

- [ ] 3.1 Create `CombatRepository` using `IDbContextFactory<AppDbContext>` (short-lived contexts), all reads/commands 3-key ownership-scoped (Id/CampaignId/UserId) like `CampaignLogRepository`
- [ ] 3.2 Implement `StartAsync` (creates Active, rejects when one is already Active — one active per campaign), `GetActiveAsync` (active combat + combatants or null), `GetByIdAsync`, `GetHistoryAsync` (ended, newest-first)
- [ ] 3.3 Implement `AddCombatantAsync`, `UpdateCombatantAsync` (single-row HP/conditions/init), `RemoveCombatantAsync`, `AdvanceTurnAsync` (bump CurrentTurnIndex, wrap→Round++), `EndAsync`, `DeleteAsync` (ownership-scoped ExecuteDelete)

## 4. Repository tests (real Postgres Testcontainer)

- [ ] 4.1 `CombatRepositoryTests` via `PostgresFixture`: start/add/get-active-with-combatants; StartAsync rejects a second active
- [ ] 4.2 Ownership negatives: foreign user/campaign not returned; update/advance/end/delete on a foreign combat = 0 rows changed
- [ ] 4.3 Advance-turn wraps + increments round; HP/conditions persist; `ConditionsJson` multi-select round-trip
- [ ] 4.4 Cascade: deleting a combat removes its combatants; deleting a campaign removes its combats; history newest-first

## 5. Service (`CombatService`)

- [ ] 5.1 Create `CombatService`: draft party from `HeroRepository.GetByCampaignAsync` → player combatants (name/HP/AC from latest sheet, HeroId set, InitiativeRoll null)
- [ ] 5.2 Draft monsters from a built encounter's monster refs → monster combatants with auto-rolled initiative (`d20 + InitiativeModifier` via `DiceRoller` + injected `IRandomSource`, default modifier 0); support manual single-add (player-style manual / monster-style auto-roll)
- [ ] 5.3 Implement `EndCombatAsync` (post-approval): per linked hero, append a new `HeroSnapshot` cloning the latest sheet with approved `CurrentHitPoints` via `SaveSnapshotAsync`; call `CombatRepository.EndAsync`; drop a `Combat`-kind `CampaignLogEntry` breadcrumb — only combatants with a HeroId write back
- [ ] 5.4 Register DI: `AddCombat` wires `CombatRepository` + `CombatService` and pulls in deps (DiceRoller, HeroRepository, CampaignLogRepository); add `AddCombat` to the consumer `Add*` chain (DI-gate rule)

## 6. Service tests (seeded RNG + real Postgres)

- [ ] 6.1 Monster initiative auto-rolls deterministically over a seeded `IRandomSource` (= seeded `d20 + modifier`)
- [ ] 6.2 Party drafting maps heroes → player combatants (HeroId, HP/AC from latest sheet, initiative unset)
- [ ] 6.3 `EndCombatAsync` appends a new `HeroSnapshot` per linked hero with the approved `CurrentHitPoints` (prior snapshots preserved), marks the combat Ended, and persists the breadcrumb; a player combatant without a HeroId writes nothing

## 7. UI — tracker component + play page

- [ ] 7.1 `CompanionUI/Components/InitiativeTracker.razor`: rehydrate the active combat from `GetActiveAsync` on load or show a Start form (name + edition); sorted combatant list with current-turn highlight + round counter; null-safe render + broad catch around payload rendering
- [ ] 7.2 Combatant controls: HP +/−, condition multi-select chips, editable initiative field; Add party, Add from encounter (fed the latest `BuiltEncounter` via page-level state from `EncounterPanel`'s built-encounter callback), Manual add, Remove; Advance turn
- [ ] 7.3 End-combat approval panel: list each player combatant before→after HP (editable) → Approve (calls `EndCombatAsync`) / Cancel (leaves combat active, writes nothing); render the ended-combat history list
- [ ] 7.4 `CompanionUI/Pages/Campaigns/CampaignTable.razor` (`@page "/campaigns/{Id:long}/table"`, `[Authorize]`): `_userId` from the NameIdentifier claim, ownership gate `CampaignRepo.GetByIdAsync(Id,userId)` + redirect; host DiceRollerPanel + EncounterPanel + CampaignLog + InitiativeTracker
- [ ] 7.5 Edit `CampaignDetail.razor`: remove the embedded DiceRollerPanel/EncounterPanel/CampaignLog, add a "▶ Run session" link to `/campaigns/{Id}/table`; keep roster + notes

## 8. Verify + finish

- [ ] 8.1 `dotnet build` 0/0 (warnings-as-errors) and run the FULL `dotnet test` suite green (incl. `FullContainerScopeValidationTests`; Docker up for Testcontainers)
- [ ] 8.2 Confirm NO `.http` / `.insomnia` change is needed (no new HTTP route / MCP tool); confirm markdown docs (if any touched) lint clean
- [ ] 8.3 Final whole-branch review on opus: trace cross-path invariants (ownership on every command, cascade on both delete paths, write-back only on approval + only for linked heroes, additive migration); reconcile plan-vs-spec drift
