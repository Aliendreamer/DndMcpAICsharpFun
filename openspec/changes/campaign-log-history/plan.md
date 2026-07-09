# Campaign Log History (Item B) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** A durable, campaign-scoped, ownership-safe timeline of rolls (auto-logged, labelled) and encounters (explicit save), with a hidden→reveal mechanic, on the campaign page.

**Architecture:** One `CampaignLogEntry` table (Kind + JSON payload) + `CampaignLogRepository` (`IDbContextFactory`, ownership-scoped) + EF migration; `DiceRollerPanel` gains persistence; new `EncounterPanel` (build+save) and `CampaignLog` (timeline+reveal) components on `CampaignDetail`.

**Tech Stack:** .NET 10, EF Core (Postgres/Npgsql), Blazor Server, xUnit + FluentAssertions + Testcontainers (`PostgresFixture`).

## Global Constraints

- `net10.0`; nullable; **warnings-as-errors**; Central Package Management. Use **Serena** for all `.cs` edits (subagent prompts: CRITICAL-Serena block + `initial_instructions`); plain Read/Edit OK for `.razor` (read existing razor for style). Run `dotnet` with `dangerouslyDisableSandbox: true`. Solution `.slnx`.
- **Ownership scoping is the security property:** every repository read/command scopes by `CampaignId` AND `UserId`; reveal/delete on a non-owned entry changes 0 rows (no leak/foreign mutation). Ship a negative test. Identity comes from the authenticated Blazor circuit, never a spoofable component parameter used AS identity (the page resolves the real `UserId` and passes it; the repository re-scopes regardless).
- **No new HTTP route, no MCP tool** → no `.http`/`.insomnia` change.
- Repository DI: `AddSingleton<CampaignLogRepository>()` in `Extensions/DatabaseExtensions.cs` (mirror `NoteRepository`). Migrations applied at startup by `MigrateDatabaseAsync`.
- Persistence tests need Docker (Testcontainers `postgres:18-alpine` via `PostgresFixture` + Respawn).

---

### Task 1: `CampaignLogEntry` entity + payloads + EF mapping + migration

**Files:**
- Create: `Domain/CampaignLogEntry.cs`, `Features/Campaigns/CampaignLogPayloads.cs`
- Modify: `Infrastructure/Persistence/AppDbContext.cs`
- Generate: a migration under `Migrations/` + updated `AppDbContextModelSnapshot.cs`

**Produces:**
- `enum CampaignLogKind { Roll, Encounter }`.
- `sealed class CampaignLogEntry { public long Id { get; set; } public long CampaignId { get; set; } public long UserId { get; set; } public CampaignLogKind Kind { get; set; } public string? Label { get; set; } public bool Hidden { get; set; } public DateTime CreatedAt { get; set; } public string PayloadJson { get; set; } = ""; }`
- `sealed record RollLogPayload(string Expression, string Breakdown, int Total, IReadOnlyList<int> Dice, IReadOnlyList<int> Kept, string Mode);`
- `sealed record EncounterMonsterLog(string Id, string Name, double Cr, int Xp);`
- `sealed record EncounterLogPayload(string Difficulty, int TotalXp, int AdjustedXp, IReadOnlyList<int> PartyLevels, IReadOnlyList<EncounterMonsterLog> Monsters, bool FullyMatched, string? Note);`

- [ ] **Step 1: Add the entity + payload records** (Serena create_text_file).
- [ ] **Step 2: Map in `AppDbContext`** — add `public DbSet<CampaignLogEntry> CampaignLogEntries => Set<CampaignLogEntry>();` and in `OnModelCreating` a config block mirroring the existing style:

```csharp
modelBuilder.Entity<CampaignLogEntry>(e =>
{
    e.HasKey(x => x.Id);
    e.Property(x => x.Kind).HasConversion<string>();
    e.Property(x => x.PayloadJson).HasColumnType("text");
    e.Property(x => x.Label);
    e.HasIndex(x => new { x.CampaignId, x.UserId, x.CreatedAt });
});
```

- [ ] **Step 3: Generate the migration** — from repo root: `dotnet ef migrations add AddCampaignLogEntries` (with `dangerouslyDisableSandbox`; uses `AppDbContextDesignTimeFactory`). If `dotnet ef` isn't installed, `dotnet tool restore` or `dotnet tool install --global dotnet-ef` first (note in report). Open the generated migration: confirm `Up()` creates the `CampaignLogEntries` table (Id identity, the columns, the index) and `Down()` drops it. **Do not hand-edit the snapshot** — let the tool update `AppDbContextModelSnapshot.cs`.
- [ ] **Step 4: `dotnet build` 0/0.** Commit `feat(campaign-log): CampaignLogEntry entity + EF mapping + migration`.

---

### Task 2: `CampaignLogRepository` (real-Postgres TDD)

**Files:**
- Create: `Features/Campaigns/CampaignLogRepository.cs`
- Modify: `Extensions/DatabaseExtensions.cs` (register)
- Test: `DndMcpAICsharpFun.Tests/Persistence/CampaignLogRepositoryTests.cs` (or wherever `NoteRepositoryTests` lives — mirror it)

**Produces:** `sealed class CampaignLogRepository(IDbContextFactory<AppDbContext> dbf)`:
- `Task<long> AddRollAsync(long userId, long campaignId, RollResult roll, string? label, bool hidden = false)` — build a `RollLogPayload` from `roll` (`roll.Expression` → a display string, `roll.Breakdown`, `roll.Total`, `roll.Dice`, `roll.Kept`, `roll.Expression.Mode.ToString()`), `JsonSerializer.Serialize`, insert `CampaignLogEntry{Kind=Roll, ...}`, return Id. `CreatedAt = DateTime.UtcNow`.
- `Task<long> AddEncounterAsync(long userId, long campaignId, EncounterLogPayload payload, string? label, bool hidden)` — serialize payload, insert `Kind=Encounter`, return Id.
- `Task<List<CampaignLogEntry>> GetByCampaignAsync(long campaignId, long userId)` — `Where(CampaignId==... && UserId==...).OrderByDescending(CreatedAt).ThenByDescending(Id).ToListAsync()`.
- `Task RevealAsync(long id, long campaignId, long userId)` — `Where(Id==id && CampaignId && UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Hidden, false))`.
- `Task DeleteAsync(long id, long campaignId, long userId)` — `Where(Id==id && CampaignId && UserId).ExecuteDeleteAsync()`.
- Payload (de)serialization helpers: `static RollLogPayload? ReadRoll(CampaignLogEntry)` / `ReadEncounter(...)` (or leave to callers).

- [ ] **Step 1: Failing tests** (mirror `NoteRepositoryTests`/`ChatRepositoryTests` — same `PostgresFixture`/`TestDb` harness; find it with Serena). Cover:
  - Seed a campaign owned by user 1 (create the User + Campaign rows the FK needs — mirror how NoteRepositoryTests seeds). `AddRollAsync(1, camp, rollResult, "Deception")` + `AddEncounterAsync(1, camp, encPayload, "Boss fight", hidden:true)`.
  - `GetByCampaignAsync(camp, 1)` → 2 entries, newest-first; the roll entry's payload deserializes to the original expression/total/dice; the encounter entry deserializes to the original difficulty/monsters.
  - **Ownership negative:** `GetByCampaignAsync(camp, 2)` (user 2) → empty; `RevealAsync(encId, camp, 2)` → the entry stays Hidden==true (0 rows); `DeleteAsync(rollId, camp, 2)` → the roll still present.
  - `RevealAsync(encId, camp, 1)` → Hidden==false; `DeleteAsync(rollId, camp, 1)` → gone.
  - Build a `RollResult` for the test via the real `DiceRoller` + a scripted `IRandomSource`, or construct one directly.
- [ ] **Step 2: Run — verify fail** (types missing / no table). **Step 3: Implement + register in DI; make pass** (Docker up). **Step 4: build 0/0.**
- [ ] **Step 5: Commit** `feat(campaign-log): ownership-scoped CampaignLogRepository (add/get/reveal/delete)`.

---

### Task 3: `DiceRollerPanel` auto-logs labelled rolls

**Files:**
- Modify: `CompanionUI/Components/DiceRollerPanel.razor`

- [ ] **Step 1:** Add `[Parameter] public long CampaignId { get; set; }`, `[Parameter] public long UserId { get; set; }`, `[Parameter] public EventCallback OnLogged { get; set; }`. Add an optional **Label** text field bound to `_label` + a set of quick-pick label buttons/`<select>` (Attack, Damage; the skill checks + ability saves from the spec). `@inject DndMcpAICsharpFun.Features.Campaigns.CampaignLogRepository LogRepo`.
- [ ] **Step 2:** In `Roll()`, after a successful `Roller.Roll`, if `CampaignId > 0`: `await LogRepo.AddRollAsync(UserId, CampaignId, res, string.IsNullOrWhiteSpace(_label) ? null : _label, hidden: false); await OnLogged.InvokeAsync();`. Keep the ephemeral recent list. Guard so a repo error shows `_error` and never throws to the circuit (wrap the log call in try/catch → `_error`).
- [ ] **Step 3:** `dotnet build` 0/0. (Wiring `CampaignId`/`UserId` from the page happens in Task 4.3.) Commit `feat(campaign-log): DiceRollerPanel auto-logs labelled rolls`.

---

### Task 4: `EncounterPanel` + `CampaignLog` components, wired on CampaignDetail

**Files:**
- Create: `CompanionUI/Components/EncounterPanel.razor`, `CompanionUI/Components/CampaignLog.razor`
- Modify: `CompanionUI/Pages/Campaigns/CampaignDetail.razor`

- [ ] **Step 1: `EncounterPanel.razor`** — `[Parameter] long CampaignId`, `[Parameter] long UserId`, `[Parameter] EventCallback OnSaved`. `@inject EncounterDesignService EncounterSvc` + `@inject CampaignLogRepository LogRepo`. Form: difficulty `<select>` (Easy/Medium/Hard/Deadly), edition `<select>` (2014/2024), theme text, label text, hidden checkbox. **Build** → `try { _built = await EncounterSvc.BuildForUserAsync(UserId, CampaignId, partyLevels:null, ParseDifficulty(_diff), ParseEdition(_ed), _theme, ct); } catch (Exception ex) { _error = ex.Message; }` → render `_built` (difficulty + monster names + not-fully-matched note). **Save to log** (enabled when `_built` set) → build `EncounterLogPayload` from `_built.Assessment` (Difficulty.ToString(), TotalMonsterXp, AdjustedXp, party levels, monsters→`EncounterMonsterLog`, FullyMatched, Note) → `await LogRepo.AddEncounterAsync(UserId, CampaignId, payload, _label, _hidden); await OnSaved.InvokeAsync();`. Reuse `ParseDifficulty`/`ParseEdition` logic (small local copies or a shared helper — check if the ones in `DndChatService` are reusable/internal; if not, small local statics are fine).
- [ ] **Step 2: `CampaignLog.razor`** — `[Parameter] long CampaignId`, `[Parameter] long UserId`. `@inject CampaignLogRepository LogRepo`. `OnInitializedAsync` / a public `RefreshAsync()` → `_entries = await LogRepo.GetByCampaignAsync(CampaignId, UserId)`. Render newest-first: for `Kind==Roll` deserialize `RollLogPayload` → `label · breakdown · total`; for `Kind==Encounter` deserialize `EncounterLogPayload` → `label · difficulty · monster names`. Hidden entries: badged/greyed + a **Reveal** button (`await LogRepo.RevealAsync(e.Id, CampaignId, UserId); await RefreshAsync();`) and a **Delete** button (`DeleteAsync` then refresh).
- [ ] **Step 3: Embed on `CampaignDetail.razor`** — resolve the authenticated `UserId` (the page injects `AuthenticationStateProvider Auth`; get the `NameIdentifier` claim → long, as other pages do). Add `<EncounterPanel CampaignId="Id" UserId="_userId" OnSaved="RefreshLog" />`, `<DiceRollerPanel CampaignId="Id" UserId="_userId" OnLogged="RefreshLog" />` (update the existing embed), and `<CampaignLog @ref="_log" CampaignId="Id" UserId="_userId" />`; `RefreshLog()` calls `_log.RefreshAsync()`. Ensure `_userId` is loaded before rendering the components (guard with the existing `@if (_campaign is null)` loading gate or similar).
- [ ] **Step 4: `dotnet build` 0/0** (razor compiles + bindings valid). Grep-confirm no HTTP/MCP/`.http` change. Commit `feat(campaign-log): EncounterPanel + CampaignLog timeline on the campaign page`.

---

### Task 5: Verify + review

- [ ] **Step 1: Full build 0/0 + full suite** green (incl. Testcontainers — repo tests + migration). `dotnet format "./DndMcpAICsharpFun.slnx" --verify-no-changes` — normalize new files, commit the format.
- [ ] **Step 2: Drive it (per `run`)** — if the stack is up: open a campaign, roll `1d20` labelled "Deception" → it appears in the log; build + save a **hidden** Hard encounter → badged hidden → Reveal un-hides it; delete an entry. (Playwright: login + navigate + act in ONE `browser_run_code_unsafe` call — the MCP browser context drops auth between tool calls.) Defer honestly if no live run; the real-Postgres repo tests carry ownership + persistence correctness.
- [ ] **Step 3: Whole-branch review (opus)** — cross-check every ADDED requirement; EMPHASIZE ownership scoping (GetByCampaign/Reveal/Delete never leak or mutate another user's entries), payload round-trip, migration correctness, rolls-auto-logged, encounters-explicit-save, hidden/reveal. Address findings. Then Items A + B are both done → finish steps (archive dice-roller + campaign-log-history → skill-optimizer → roadmap).
