## Why

Companion table-play (Item B), building on the dice roller (Item A) and encounter design. During a
campaign, rolls and encounters happen constantly and are worth keeping — a campaign timeline you can
scroll back through, with the ability to prep something secretly and **reveal** it later. Item A's
rolls are ephemeral and encounters have no persistence; this adds a durable, campaign-scoped log for
both.

## What Changes

- Add one unified persisted entity **`CampaignLogEntry`** (a JSON-payload timeline):
  `{ Id, CampaignId, UserId, Kind (Roll | Encounter), Label?, Hidden, CreatedAt, PayloadJson }`.
  `Label` is the roll's reason/skill (`"Deception"`, `"Wisdom save"`, `"Fire damage"`, `"Attack"`)
  or an encounter note. `PayloadJson` (a `text` JSON column) holds the roll (expression, breakdown,
  total, dice, kept, mode) or the encounter (difficulty, XP, party levels, monsters, note). New EF
  `DbSet` + a **migration** applied at startup.
- Add **`CampaignLogRepository`** (`IDbContextFactory`, mirroring `NoteRepository`):
  `AddRollAsync`, `AddEncounterAsync`, `GetByCampaignAsync` (newest-first, ownership-checked),
  `RevealAsync` (flip `Hidden→false`), `DeleteAsync` — all campaign + user scoped.
- **Rolls auto-log with a label:** `DiceRollerPanel` (Item A) gains a `CampaignId` parameter and an
  optional **Label** field (free text + quick-pick tags: Attack, Damage; skill checks
  Deception/Insight/Intimidation/…; ability saves STR…CHA). Every roll auto-persists a `Roll` entry
  (revealed) with its label — nothing is dropped.
- **Encounters explicitly saved:** a new **`EncounterPanel`** component on the campaign page (a
  build-encounter form using `EncounterDesignService.BuildForUserAsync`) shows the built encounter
  and offers **Save to log** with a secret/hidden checkbox + optional label.
- **Timeline UI:** a **`CampaignLog`** component on `CampaignDetail` shows all entries newest-first;
  hidden entries render with a hidden badge + a **Reveal** button (flips `Hidden`), plus optional
  delete.
- **No new HTTP route / no MCP tool** — server-side Blazor components call the repository + the
  existing encounter service → no `.http`/`.insomnia` change.

## Capabilities

### New Capabilities

- `campaign-log-history`: the `CampaignLogEntry` entity + migration, `CampaignLogRepository`
  (ownership-scoped), auto-logged labelled rolls, the explicit encounter save panel, and the
  hidden/reveal campaign timeline.

### Modified Capabilities

- (none — `DiceRollerPanel` gains an additive `CampaignId`/label + persistence; no existing spec's
  requirements change. The dice-roller spec's "ephemeral, no DB" scenario was explicitly this-slice
  scoped and this change supersedes it by design, tracked here as the follow-on Item B.)

## Impact

- **Code:** new `Domain` entity + `Features/Campaigns/CampaignLogRepository.cs`; `AppDbContext`
  `DbSet` + mapping + an EF migration (generated via `AppDbContextDesignTimeFactory`, applied by
  `MigrateDatabaseAsync`); new `CompanionUI/Components/{EncounterPanel,CampaignLog}.razor`;
  `DiceRollerPanel` updated to take `CampaignId` + label + persist.
- **APIs:** none — no HTTP route, no MCP tool, no auth surface change. Persistence is server-side in
  the Blazor circuit, ownership-scoped by `CampaignId` + `UserId`.
- **Data:** a new `CampaignLogEntries` table (migration). Rolls auto-write on every roll; encounters
  write on explicit save.
- **Docs:** none (`.http`/`.insomnia` unchanged).
