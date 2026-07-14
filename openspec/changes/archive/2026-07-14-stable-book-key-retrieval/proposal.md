## Why

`ask_rules` and `plan_downtime` scope their retrieval to source books by **display-name string**
(`"Dungeon Master's Guide 2014"`), but `dnd_blocks` has **zero DMG blocks** — the DMG was never
ingested — so those features silently retrieve and cite nothing from the DMG. Worse, the whole scoping
mechanism is fragile: blocks are filtered by the display name, which is free text prone to
apostrophe/spacing drift (the PHB already landed as `"PlayerHandbook 2014"`), and a mismatch fails
**silently** — empty results, no error. Meanwhile `dnd_entities` already filters by the stable 5etools
key (`PHB`, `DMG`), so blocks are the inconsistent, fragile side.

## What Changes

- **Stable `source_key` on every block.** Add a `source_key` payload field to `dnd_blocks`
  (`PHB`/`MM`/`DMG`/`XGE`/`ERLW`), written at ingest from the record's `FivetoolsSourceKey`, with a
  Qdrant payload index. `source_book` (display name) stays for citations.
- **`BookCatalog`** — a single key ↔ display-name registry for the five books, the one source of truth
  for the backfill mapping, the scope constants, and the guard.
- **Backfill** existing blocks: a one-time `POST /admin/retrieval/backfill-source-keys` set-payloads
  `source_key` onto all current blocks by their `source_book` display name (no re-embed).
- **Retrieval filters by key.** `RagRetrievalService`'s scope filter matches `source_key`;
  `RetrievalQuery.SourceBooks` → `SourceKeys`; `RuleSources`/`DowntimeSources`/`SettingCatalog` become
  key lists. Kills the display-name fragility.
- **DMG ingest.** Register + `ingest-blocks` the DMG (`fivetoolsSourceKey=DMG`,
  `displayName="Dungeon Master's Guide 2014"`) so DMG blocks land with `source_key=DMG`.
- **Guard.** Startup log-warning when any scope key has 0 blocks (catches the missing-ingest class in
  the live corpus); unit test that scope keys ⊆ `BookCatalog`.
- **Registry reconcile.** `POST /admin/books/reconcile` (metadata-only) creates the orphaned
  MM/XGE/ERLW `IngestionRecords` the DB reset dropped (blocks already in Qdrant), so the registry is
  truthful again.

## Capabilities

### New Capabilities

- `stable-book-key-retrieval`: `source_key`-based block scoping, `BookCatalog`, the source-key backfill,
  the scope-health startup guard, and the metadata-only registry reconcile.

### Modified Capabilities

<!-- Rules-adjudication and downtime-advisor retrieval scoping moves from display-name to key, but
their observable contract (cited passages from the named books) is unchanged. -->

## Impact

- **Code:** `dnd_blocks` payload (`source_key`) + Qdrant index; `BlockIngestionOrchestrator` /
  `BlockMetadata` / `QdrantPayloadMapper`; `IVectorStoreService` (facet + set-payload-by-book);
  `RagRetrievalService` filter; `RetrievalQuery`; `RuleSources`/`DowntimeSources`/`SettingCatalog`;
  new `BookCatalog`, backfill + reconcile services and admin endpoints; startup scope-health hosted
  step. `.http` + `.insomnia.json` updated for the two new endpoints.
- **Data:** one-time `source_key` backfill of ~16,698 existing blocks (payload update, no re-embed);
  DMG blocks newly ingested; MM/XGE/ERLW `IngestionRecords` re-created.
- **Sequencing:** ingest writes `source_key` → backfill existing → *then* switch the retrieval filter,
  so no block is unmatched mid-migration.
- **No** new migration to `AppDbContext` schema (reconcile inserts existing `IngestionRecord` rows).
