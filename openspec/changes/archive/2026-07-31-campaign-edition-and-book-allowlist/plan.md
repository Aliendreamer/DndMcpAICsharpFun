# Plan — remaining tasks 5 (UI) and 6 (contracts + gates)

Tasks 1–4 shipped in commit `2f1c104` (domain, JSON column + migration, defaults/validation,
`CampaignScopeResolver`, tests). What follows is the rest of the change.

## Context an implementer needs

- `Campaign` carries `Edition` (`DndVersion`, default `Edition2014`) and `AllowedBookKeys`
  (`IReadOnlyList<string>`, `BookCatalog` KEYS, default `[]`).
- `CampaignRepository.CreateAsync(userId, name, description, setting = null, edition = Edition2014,
  allowedBookKeys = null)` — a `null` allowlist means "core-only for the edition"
  (`CampaignScopeResolver.CoreBooksFor`); an explicit list is preserved verbatim.
- `CampaignRepository.SetBooksAsync(id, userId, edition, allowedBookKeys)` — the update path. It has
  **no caller yet**; the detail page is its first.
- Both write paths reject an unknown key with `ArgumentException` naming it.
- Books come from `BookCatalog.All` (`Key`, `DisplayName`, `Version`, `IsCore`) — 19 entries, three of
  them the not-yet-ingested revised core (`XPHB`/`XDMG`/`XMM`, `Edition2024`).
- Campaigns have **no HTTP endpoint** — the UI talks to `CampaignRepository` directly.

## Task 5.1 — create form (`CompanionUI/Pages/Campaigns/Campaigns.razor`)

- Add an **Edition** `<select>` (2014 / 2024) bound to a `DndVersion` field, following the existing
  `Setting` select's markup.
- Add a **book multi-select**: a checkbox per `BookCatalog.All` entry, labelled with `DisplayName`,
  grouped by edition so the revised core is visibly separate. Selection state is a
  `HashSet<string>` of keys.
- Seed the selection from `CampaignScopeResolver.CoreBooksFor(edition)`, and **re-seed it when the
  edition changes** — nothing is lost before creation, and it mirrors the create-time default.
- Pass the selected keys to `CreateAsync`. Reset the form state after a successful create, as the
  existing fields do.
- Catch the repository's `ArgumentException` into the existing `_createError` (a stale UI list vs the
  catalog must surface, not throw an unhandled circuit error).

## Task 5.2 — detail form (`CompanionUI/Pages/Campaigns/CampaignDetail.razor`)

- Same edition select + book checkboxes next to the existing `Setting` select, initialised from the
  loaded campaign.
- **Do NOT re-seed the books when the edition changes here** — that would silently drop supplements a
  live campaign is already using. Edition and allowlist are independent by design.
- Persist via `SetBooksAsync`, then refresh the local campaign so the UI reflects stored state.

## Task 5.3 — CSS

- Every new class must resolve in `wwwroot/app.css`. Note `.create-form label` is
  `flex-direction: column`, so a checkbox row needs its own modifier (the `.edit-grid
  label.checkbox-label` pattern is the precedent).
- Keep the book list scrollable/bounded so 19 checkboxes do not dominate the form.

## Task 5.4 — live screenshots (controller, not a subagent)

- `docker compose -f docker-compose.yml up -d --build app` FIRST (the image does not hot-reload).
- Log in as `test`/`test`, screenshot the campaign create form and the detail page at ~1280 and ~390,
  and run the `browser_evaluate` horizontal-overflow check.

## Task 6 — contracts and gates

- 6.1 `.http` / insomnia: **verify, don't edit.** There is no campaign endpoint (`MapGet/Post/Put/
  Delete`) anywhere — confirm with a search and record the finding in the task list rather than
  inventing a route.
- 6.2 `dotnet build` 0/0; FULL `dotnet test` (Docker up — the migration test needs it);
  `dotnet format --verify-no-changes` on the changed files only.
- 6.3 Confirm no consumer changed: `RuleSources`, `DowntimeSources`, `SettingCatalog` and the chat
  tool schemas untouched, and the suite count moves only by this change's own tests.
- Tick every completed box in `tasks.md`, including 1–4, which shipped unticked.
