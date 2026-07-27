## 1. Domain + persistence (one atomic task — a record change ripples to every constructor)

- [ ] 1.1 Add `Edition` (`DndVersion`, non-null, default `Edition2014`) and `AllowedBookKeys` (`IReadOnlyList<string>`, default empty) to the `Campaign` record. `find_referencing_symbols` on `Campaign` FIRST and update every construction site in the same task — a positional record change breaks callers, so a split leaves a red commit.
- [ ] 1.2 Map `AllowedBookKeys` as a JSON column in `AppDbContext` (the `HeroSnapshot.CharacterSheet` precedent); leave the existing `UserId` index untouched.
- [ ] 1.3 `dotnet ef migrations add AddCampaignEditionAndBooks`. OPEN the migration: `Up()` must add ONLY the two columns to `Campaigns`, `Down()` drop only them. Read the `AppDbContextModelSnapshot` diff — it must be a pure insertion touching no other table; anything else means pre-existing model drift is being captured now.

## 2. Defaults and validation

- [ ] 2.1 Core-only default on create: `Edition2014` → `PHB/DMG/MM`, `Edition2024` → `XPHB/XDMG/XMM`, applied only when no allowlist is supplied. Derive the trio from `BookCatalog` (filter by `Version` + a core marker), not a second hardcoded list that can drift from the catalog.
- [ ] 2.2 Reject an allowlist entry that is not a `BookCatalog` key, on both create and update, with a message naming the offending key.
- [ ] 2.3 Tests: new 2014 campaign gets exactly the 2014 core; new 2024 campaign gets exactly the revised core; an explicit allowlist is preserved verbatim (defaults do NOT overwrite it); an unknown key is rejected on create AND on update.

## 3. Migration of existing campaigns

- [ ] 3.1 Seed every pre-existing campaign with `Edition2014` and every source-book key registered AT MIGRATION TIME — read the keys, do not hardcode a list that will rot.
- [ ] 3.2 Real-Postgres test (Testcontainers + Respawn, the existing `PostgresFixture`): insert a campaign in the pre-migration shape, run the migration, assert it ends with `Edition2014` and every current key. Assert BEHAVIOUR PRESERVED (the same books are available as before), not a magic count.
- [ ] 3.3 Test that the core-only default is NOT applied retroactively — an existing campaign must not come out with three books.

## 4. The resolver

- [ ] 4.1 Add a `CampaignScopeResolver` exposing, for a campaign: its `Edition`, its `LoreScope` (allowlist as-is) and its `MechanicsScope` (allowlist ∩ books whose `BookCatalog` version equals the campaign edition). Pure function over stored state — no DB, no model call.
- [ ] 4.2 Tests: a 2024 campaign allowing `XPHB/XDMG/ERLW` yields mechanics `{XPHB, XDMG}` and lore `{XPHB, XDMG, ERLW}`; an all-2014 campaign yields identical mechanics and lore scopes; an empty allowlist yields empty scopes (honest-empty, NOT "everything").
- [ ] 4.3 Guard test: the resolver has no dependency on `IChatClient` or any retrieval service — scope is decided by state, never by a model.

## 5. UI

- [ ] 5.1 Edition selector (2014/2024) and a book multi-select sourced from `BookCatalog` on the campaign create/edit form, following existing form markup and classes.
- [ ] 5.2 Every class the new markup references must resolve to a rule in `wwwroot/app.css` — grep the classes; an undefined class renders as raw HTML.
- [ ] 5.3 Screenshot the campaign form at desktop (~1280) AND mobile (~390) after `docker compose -f docker-compose.yml up -d --build app`, and run the `browser_evaluate` horizontal-overflow check. The image does not hot-reload; a stale container silently screenshots the old markup.

## 6. Contracts + gates

- [ ] 6.1 Update `DndMcpAICsharpFun.http` AND `dnd-mcp-api.insomnia.json` for the campaign create/update payload change, in the same commit.
- [ ] 6.2 `dotnet build` 0/0; FULL `dotnet test` INCLUDING persistence (Docker required — the migration test needs it); `dotnet format --verify-no-changes` clean for changed files.
- [ ] 6.3 Confirm NO consumer changed: `RuleSources`, `DowntimeSources`, `SettingCatalog` and the chat tool schemas are untouched, and the full suite's pre-existing count is unchanged apart from the new tests. This slice must alter no answer.
