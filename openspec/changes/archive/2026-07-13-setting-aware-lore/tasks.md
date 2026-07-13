## 1. Phase 1 — ingest a setting book (ops, existing endpoints, no code)

- [ ] 1.1 Confirm the ERLW PDF is registered (or register it via `POST /admin/books/register` with `fivetoolsSourceKey=ERLW`); capture the resolved `SourceBook` key the corpus uses for it
- [ ] 1.2 `POST /admin/books/{id}/ingest-blocks` for ERLW; verify `dnd_blocks` now returns ERLW-sourced blocks (spot-check via `GET /retrieval/search?q=Sharn&sourceBook=<ERLW key>`)

## 2. Setting catalog

- [ ] 2.1 (TDD) Tests: `Eberron` → `{ERLW}` ∪ core rulebooks; `Generic`/null/unknown → empty (unscoped) scope
- [ ] 2.2 `Features/Lore/SettingCatalog` — in-code registry (setting key → source-book keys, always union core); `Resolve(setting)` → book set (empty for generic/unknown)

## 3. Multi-source-book OR filter (rag-retrieval)

- [ ] 3.1 (TDD) Unit test: `RagRetrievalService` with a `SourceBooks` set of ≥2 builds a Qdrant OR/should condition over exactly those books; single-element set == existing single filter; empty set == no restriction
- [ ] 3.2 Add `SourceBooks` (set) to `RetrievalQuery`; apply it as an OR condition in `RagRetrievalService` (keep the existing single `SourceBook` for endpoint back-compat)
- [ ] 3.3 (integration) Real-Qdrant test: seed blocks from an in-set book + an off-setting book; a scoped query returns the in-set block and EXCLUDES the off-setting block (non-vacuity — fails if the filter is a no-op)

## 4. Campaign.Setting persistence

- [ ] 4.1 (TDD, real Postgres) Test: create/update a campaign with a setting round-trips; null default preserved
- [ ] 4.2 Add nullable `Setting` (catalog-key string) to `Campaign`; thread through `CampaignRepository` create/update; additive EF migration (applied at startup)

## 5. Campaign form setting selector (UI)

- [ ] 5.1 Add a setting `<select>` (populated from the catalog, plus a "Generic/none" option) to the campaign create/edit form; bind + persist via `CampaignRepository`

## 6. SettingLoreService (grounded, scoped, ownership-gated)

- [ ] 6.1 (TDD) Tests: ownership negative (`GetByIdAsync(id, userId)` foreign/missing → throws, no leak); resolves setting → scoped `RetrievalQuery.SourceBooks`; returns passages with citations (source book + section/title); empty scope (Generic) → unscoped; empty retrieval → explicit empty result
- [ ] 6.2 `Features/Lore/SettingLoreService.AskForUserAsync(userId, campaignId, question, ct)` — ownership check, resolve setting via catalog, scoped prose RAG, project to cited passages / explicit empty

## 7. ask_setting_lore chat tool

- [ ] 7.1 Register `ask_setting_lore(campaignId, question)` in `DndChatService` (closes over session `userId`); description binds the grounding contract (answer only from returned cited passages; say the setting's sources don't cover it when empty)
- [ ] 7.2 (TDD) BOTH guard tests: add to the `userId`-not-exposed schema filter AND the authenticated-present / unauthenticated-absent presence lists; a routing test that a foreign `campaignId` throws (ownership reaches the service)

## 8. Verification

- [ ] 8.1 Build 0/0 + full `dotnet test` green (real Postgres + Qdrant)
- [ ] 8.2 Live smoke (after Phase 1 + container rebuild): set a campaign to Eberron via the form (persists); ask "what are the Dragonmarked Houses?" in chat → grounded, cited answer sourced from ERLW; a Generic campaign answers unscoped; an off-setting question returns honest-empty. Setting `<select>` persists; no horizontal overflow desktop + mobile
