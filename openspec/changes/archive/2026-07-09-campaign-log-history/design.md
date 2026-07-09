## Context

Persistence is one EF `AppDbContext` on Postgres; repositories use `IDbContextFactory` for
short-lived, Blazor-safe contexts (`NoteRepository` is the closest model — campaign-scoped,
`UserId`+`CampaignId`). JSON is stored either via `HasConversion` (typed) or a plain `text` column
holding a JSON string (`StructuredTable.ColumnsJson` etc.); enums via `HasConversion<string>()`.
Schema is created by EF migrations applied at startup (`MigrateDatabaseAsync` → `db.Database
.MigrateAsync()`), generated with `AppDbContextDesignTimeFactory`. Item A's `DiceRollerPanel` is on
`CampaignDetail` and ephemeral; encounter results (`BuiltEncounter`/`EncounterAssessment`) come from
`EncounterDesignService.BuildForUserAsync(userId, campaignId, …)`, which is already ownership-gated.

## Goals / Non-Goals

**Goals:**

- One durable, campaign-scoped, ownership-safe timeline for both rolls and encounters, with a
  hidden→reveal mechanic.
- Rolls auto-logged (with a label) so nothing is lost; encounters saved explicitly.
- Reuse the existing repository/JSON/migration patterns and the existing encounter service.

**Non-Goals:**

- Sharing/multi-user reveal (single-user tool; "hidden" is the owner's own secret prep). No
  real-time push.
- New HTTP endpoints or MCP tools — the UI components call the repository + service in-process.
- Editing a logged roll/encounter (only reveal + delete). Advanced filtering/search of the log.

## Decisions

### One unified `CampaignLogEntry` (Kind + JSON payload), not two typed tables

The timeline query, the hidden/reveal flag, ownership scoping, and rendering are identical for rolls
and encounters; only the payload differs. A single table keyed by `Kind` with a `text PayloadJson`
column gives one query, one repository, one migration, and trivial extensibility (a future `Note`
or `Initiative` kind). `PayloadJson` is a plain `text` column (JSON string) — the repository
serializes/deserializes typed payload records (`RollLogPayload`, `EncounterLogPayload`); `Kind` maps
via `HasConversion<string>()`. *Alternative considered:* separate `RollLog`/`EncounterLog` tables —
rejected; duplicated timeline/ownership/reveal code for no query benefit at this scale.

### Ownership scoping in the repository, not just the UI

Every query/command scopes by `CampaignId` AND `UserId`: `GetByCampaignAsync(campaignId, userId)`
returns only that user's entries in that campaign; `RevealAsync(id, campaignId, userId)` /
`DeleteAsync(id, campaignId, userId)` use `WHERE Id==id && CampaignId==campaignId && UserId==userId`
(`ExecuteUpdate`/`ExecuteDelete`), so an entry not owned affects 0 rows (a no-op, never a leak or a
foreign mutation). This mirrors `NoteRepository` + the SEC-08 ownership stance; a negative test
proves user B can't reveal/delete/read user A's entries. The signed-in `UserId` comes from the
authenticated Blazor circuit (`AuthenticationStateProvider` / claims), never a component parameter a
caller controls.

### Rolls auto-log; encounters save explicitly

`DiceRollerPanel` gains `[Parameter] long CampaignId` and a `[Parameter] long UserId` (or resolves
the user from auth) and an optional label field. On each successful roll it calls
`CampaignLogRepository.AddRollAsync(userId, campaignId, result, label, hidden:false)` in addition to
the in-memory recent list — every roll persisted, revealed. The new `EncounterPanel` builds via the
encounter service and only writes on an explicit **Save to log** click (with the hidden checkbox +
label). This matches the chosen "auto-log rolls, save encounters" trigger and keeps the log
signal-rich without an encounter flood.

### Reveal + delete only; payload is immutable

A logged entry is a historical fact — the only mutations are `Hidden→false` (reveal) and delete. No
edit path (avoids "which roll really happened?" ambiguity). Hidden entries are still visible to the
owner (their campaign) but badged and greyed with a Reveal action — the mechanic is prep-then-surface,
not access control.

## Risks / Trade-offs

- **Auto-logging every roll → table growth.** Rolls are tiny rows; at single-user personal volume
  this is negligible. *Mitigation:* delete is available; a retention/prune policy is a future option,
  not needed now.
- **PayloadJson schema drift.** Payload records evolve; deserialization must tolerate older rows.
  *Mitigation:* payload records are additive/nullable-friendly, deserialized with default options;
  a round-trip test guards the current shape.
- **Migration must apply cleanly at startup and in Testcontainers.** *Mitigation:* generate the
  migration with the design-time factory, commit the migration + `AppDbContextModelSnapshot`; the
  real-Postgres repository tests exercise the applied schema, catching a bad migration.
- **Component ↔ auth wiring.** The panels need the authenticated `UserId`; resolving it wrong would
  break ownership. *Mitigation:* resolve from `AuthenticationStateProvider` on the page (as
  `CampaignDetail` already does) and pass the verified id down, never trust a route/param for
  identity; the repository re-scopes by `UserId` regardless.
