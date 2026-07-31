## 1. Domain + persistence (one atomic task — a record change ripples to every constructor)

- [x] 1.1 Add `Edition` (`DndVersion`, non-null, default `Edition2014`) and `AllowedBookKeys` (`IReadOnlyList<string>`, default empty) to the `Campaign` record. `find_referencing_symbols` on `Campaign` FIRST and update every construction site in the same task — a positional record change breaks callers, so a split leaves a red commit.
- [x] 1.2 Map `AllowedBookKeys` as a JSON column in `AppDbContext` (the `HeroSnapshot.CharacterSheet` precedent); leave the existing `UserId` index untouched.
- [x] 1.3 `dotnet ef migrations add AddCampaignEditionAndBooks`. OPEN the migration: `Up()` must add ONLY the two columns to `Campaigns`, `Down()` drop only them. Read the `AppDbContextModelSnapshot` diff — it must be a pure insertion touching no other table; anything else means pre-existing model drift is being captured now.

## 2. Defaults and validation

- [x] 2.1 Core-only default on create: `Edition2014` → `PHB/DMG/MM`, `Edition2024` → `XPHB/XDMG/XMM`, applied only when no allowlist is supplied. Derive the trio from `BookCatalog` (filter by `Version` + a core marker), not a second hardcoded list that can drift from the catalog.
- [x] 2.2 Reject an allowlist entry that is not a `BookCatalog` key, on both create and update, with a message naming the offending key.
- [x] 2.3 Tests: new 2014 campaign gets exactly the 2014 core; new 2024 campaign gets exactly the revised core; an explicit allowlist is preserved verbatim (defaults do NOT overwrite it); an unknown key is rejected on create AND on update.

## 3. Migration of existing campaigns

- [x] 3.1 Seed every pre-existing campaign with `Edition2014` and every source-book key registered AT MIGRATION TIME — read the keys, do not hardcode a list that will rot. VERIFIED-WITH-CAVEAT (task 6): the shipped migration hardcodes the 16 Edition2014 keys instead of reading `BookCatalog` live, and its own comment defends this deliberately ("a migration is a frozen historical snapshot"). The hardcoded list matches `BookCatalog.All` filtered to `Edition2014` exactly as of authoring time, so the OUTCOME is correct even though the MECHANISM contradicts this box's literal wording — ticked on outcome, flagged for a future reader.
- [x] 3.2 Real-Postgres test (Testcontainers + Respawn, the existing `PostgresFixture`): insert a campaign in the pre-migration shape, run the migration, assert it ends with `Edition2014` and every current key. Assert BEHAVIOUR PRESERVED (the same books are available as before), not a magic count. DONE: `DndMcpAICsharpFun.Tests/Persistence/CampaignEditionMigrationTests.cs` — `Migration_preserves_the_full_pre_migration_book_access_for_an_existing_campaign`. Since the shared `PostgresFixture` database already has every migration applied and Respawn only resets data (never replays migrations over existing rows), the test creates its own throwaway database on the SAME running container, migrates it only to `20260722180055_AuditFkIndexes` (the migration immediately before this one), inserts a pre-migration-shaped row with raw SQL, then applies `20260727164652_AddCampaignEditionAndBooks` and reads the result back. Asserts `Edition2014` plus the full 16-key list by value (not `Count == 16`), with an explicit check that setting/supplement keys (`ERLW`, `XGE`, `TCE`, `SCAG`) survived. Demonstrated discriminating: temporarily narrowing the migration's seed SQL to `'["PHB","MM","DMG"]'` in a scratch edit (reverted, never committed) made this test fail with a 13-item shortfall.
- [x] 3.3 Test that the core-only default is NOT applied retroactively — an existing campaign must not come out with three books. DONE: `DndMcpAICsharpFun.Tests/Persistence/CampaignEditionMigrationTests.cs` — `Migration_does_not_retroactively_apply_the_core_only_default_to_an_existing_campaign`, using the same throwaway-database isolation as 3.2. Asserts the migrated row's `AllowedBookKeys` is neither equivalent to nor as small as `CampaignScopeResolver.CoreBooksFor(Edition2014)` (`["PHB","DMG","MM"]`), so it fails if the migration is later "simplified" to seed the new-campaign core-only default instead of the full pre-migration set — confirmed by the same scratch-sabotage run used for 3.2, which failed both assertions.

## 4. The resolver

- [x] 4.1 Add a `CampaignScopeResolver` exposing, for a campaign: its `Edition`, its `LoreScope` (allowlist as-is) and its `MechanicsScope` (allowlist ∩ books whose `BookCatalog` version equals the campaign edition). Pure function over stored state — no DB, no model call.
- [x] 4.2 Tests: a 2024 campaign allowing `XPHB/XDMG/ERLW` yields mechanics `{XPHB, XDMG}` and lore `{XPHB, XDMG, ERLW}`; an all-2014 campaign yields identical mechanics and lore scopes; an empty allowlist yields empty scopes (honest-empty, NOT "everything").
- [x] 4.3 Guard test: the resolver has no dependency on `IChatClient` or any retrieval service — scope is decided by state, never by a model.

## 5. UI

- [x] 5.1 Edition selector (2014/2024) and a book multi-select sourced from `BookCatalog` on the campaign create/edit form, following existing form markup and classes.
- [x] 5.2 Every class the new markup references must resolve to a rule in `wwwroot/app.css` — grep the classes; an undefined class renders as raw HTML.
- [x] 5.3 Screenshot the campaign form at desktop (~1280) AND mobile (~390) after `docker compose -f docker-compose.yml up -d --build app`, and run the `browser_evaluate` horizontal-overflow check. The image does not hot-reload; a stale container silently screenshots the old markup. Live-verified in-browser by the requester (edition re-seed on create, no re-seed on the detail page, both persisted correctly); not re-run for task 6.

## 6. Contracts + gates

- [x] 6.1 Update `DndMcpAICsharpFun.http` AND `dnd-mcp-api.insomnia.json` for the campaign create/update payload change, in the same commit. NO EDIT NEEDED: there is no campaign HTTP endpoint. Searched route registrations (`Map(Get|Post|Put|Delete|Group)` with a campaign path) repo-wide — zero matches; searched `Features/Mcp/DndMcpTools.cs` and `Features/Chat/DndChatService.cs` for campaign-create/update tools — only read-side `campaignId` scoping params on existing tools, none that create/update a campaign, its edition, or its allowlist. The Blazor pages call `CampaignRepository` directly (confirmed via `new CampaignRepository` call-site search). Neither `.http` nor `.insomnia.json` was touched.
- [x] 6.2 `dotnet build` 0/0; FULL `dotnet test` INCLUDING persistence (Docker required — the migration test needs it); `dotnet format --verify-no-changes` clean for changed files. Build: 0 Warning(s)/0 Error(s). Test: 1897/1897 passed, 0 failed, 0 skipped. Format: clean (exit 0, no output) on all 10 changed `.cs` files and both changed `.razor` files.
- [x] 6.2b Gates RE-RUN after the task-7 hardening pass below (the 6.2 numbers above are the
  pre-hardening ones). Build: 0 Warning(s)/0 Error(s). Test: 1910/1910 passed, 0 failed, 0 skipped
  (1899 at commit `681e7b0` + 5 hardening tests + 6 `CampaignScopeDisplayTests`). Format:
  `--verify-no-changes` clean on all 7 changed/added `.cs` files.
- [x] 6.3 Confirm NO consumer changed: `RuleSources`, `DowntimeSources`, `SettingCatalog` and the chat tool schemas are untouched, and the full suite's pre-existing count is unchanged apart from the new tests. This slice must alter no answer. Confirmed via `git diff --name-only 2f1c104~1..HEAD`: none of `Features/Rules/RuleSources.cs`, `Features/Downtime/DowntimeSources.cs`, `Features/Lore/SettingCatalog.cs`, `Features/Chat/DndChatService.cs`, `Features/Mcp/DndMcpTools.cs` appear in the diff.

## 7. Post-review hardening (follow-up on the shipped slice)

Fixes found reviewing the shipped 1–6 work, plus the two defects the task-5.3 screenshots caught only
once they were re-run against a freshly built container.

- [x] 7.1 Reject an out-of-range `DndVersion` on BOTH write paths (`CreateAsync`, `SetBooksAsync`),
  symmetric with the existing book-key validation. `Enum.TryParse` — which the Blazor circuit handlers
  use — accepts any numeric string, so a crafted `"99"` would otherwise persist a permanently-empty
  `MechanicsScope` with no error. Tests: rejected on create AND on update, message naming the value.
- [x] 7.2 Contain a corrupted `AllowedBookKeys` column. A malformed row still deserializes to an empty
  list (EF materialization must not throw and take down the whole campaign list) but is now LOGGED —
  silently-empty is indistinguishable from a DM who unchecked every book. Null elements in an
  otherwise-valid array (`'["PHB",null]'`) are FILTERED, because `CampaignScopeResolver`'s
  `HashSet<string>` and the value comparer's hash both throw on a null. Two real-Postgres tests insert
  each shape with raw SQL.
- [x] 7.3 `Edition2024Sources` `MM25` → `XMM`. `"MM25"` was never a 5etools key; the entry matched
  nothing and left the mapper disagreeing with `BookCatalog`, which registers `XMM` as `Edition2024` —
  so a 2024 campaign's `MechanicsScope` would have excluded the 2024 Monster Manual's own monsters.
  This is the pair to the already-shipped `EntityIdSlug` override fix (commit `dbe36f3`); a regression
  test now asserts the mapper and the catalog AGREE rather than each being right alone. The main spec
  `openspec/specs/entity-id-unification/spec.md` still documented the dead `MM25` mapping and was
  corrected in the same pass (registry drift — the failure class this repo keeps hitting).
- [x] 7.4 Scope summary on both forms: `Rules from: … · Lore from: N books`, derived from the CURRENT
  (possibly unsaved) form state via `CampaignScopeResolver`, so the summary can never disagree with the
  checkboxes mid-edit. Plus an explicit warning when `MechanicsScope` is empty — otherwise the
  honest-empty scope is invisible until rules answers come back with nothing. Extracted to a shared
  `CampaignScopeDisplay` (6 tests) rather than a copy in each `.razor`, which would drift; mechanics
  names are capped at 3 + "+N more" because a migrated campaign carries all 16 books and naming every
  one wrapped the detail page to three dense lines.
- [x] 7.5 CSS: `.book-select label.checkbox-label` now sets `display: flex` ITSELF. It previously
  inherited flex from `.create-form label`, so it worked on the create form and was INERT on the detail
  page (labels computed `display: block`; the `flex-direction`/`gap`/`align-items` declarations did
  nothing and the checkbox rendered jammed against its text). This is the 5.2 failure mode one level
  deeper — the class RESOLVED, but its rules could not apply — and only the live detail-page render
  showed it.
- [x] 7.6 Live re-verification after `docker compose -f docker-compose.yml up -d --build app`, at 1280
  AND 390: edition change re-seeds the create form (2024 → XPHB/XDMG/XMM) but NOT the detail page;
  a 2024 edition with ERLW checked shows `Rules from: Player's Handbook 2024 · Lore from: 2 books` (the
  two-canon split, rendered); the empty-scope warning appears when every book is unchecked; adding a
  2024 book to the 2014 campaign moved lore 16 → 17 with mechanics unchanged and SURVIVED a reload
  (`SetBooksAsync` persists). `browser_evaluate` horizontal-overflow check clean on both pages at 390
  (`scrollWidth == clientWidth`, no internal overflow).
