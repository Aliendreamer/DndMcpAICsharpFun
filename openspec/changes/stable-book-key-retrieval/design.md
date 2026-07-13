## Context

Block retrieval scoping (`ask_rules`, `plan_downtime`, setting lore) filters `dnd_blocks` by the
`source_book` **display name**. That is (a) fragile — free-text, apostrophe/spacing drift, silent empty
on mismatch — and (b) inconsistent with `dnd_entities`, which already filters by the stable 5etools key.
The DMG gap (0 blocks, scoped-but-absent) is a symptom of the fragility. This change moves block scoping
to a stable `source_key`, ingests the DMG, and adds a guard so the silent-empty class fails loudly.

Verified corpus (Qdrant facet, today): `PlayerHandbook 2014` 5243, `Monster Manual 2014` 4995,
`Eberron: Rising from the Last War` 4322, `Xanathar's Guide to Everything` 2138 — **no DMG**.
`IngestionRecords` has only PHB (a past DB reset orphaned the rest). Scope filter is built in exactly one
place (`RagRetrievalService:110-119`) and consumed by exactly three callers.

## Goals / Non-Goals

**Goals:** DMG content retrievable/citable by the scoped features; block scoping keyed by a stable
identifier, not a display string; a loud guard against scope-key/corpus drift; a truthful registry.

**Non-Goals:** re-embedding any existing block (backfill is payload-only); changing `dnd_entities`
(already keyed); a new EF schema migration; reconciling anything beyond the four books already in Qdrant;
changing the citation text the LLM produces (still the display name).

## Decisions

### D1 — `source_key` payload field (additive, non-destructive)

Blocks gain `source_key` (`QdrantPayloadFields.SourceKey = "source_key"`) alongside the existing
`source_book` (kept for display/citation). `BlockMetadata` carries `SourceKey`;
`BlockIngestionOrchestrator` sets it from `record.FivetoolsSourceKey` (already on the record);
`QdrantPayloadMapper` writes it. `QdrantCollectionInitializer` adds a keyword payload index on
`source_key` (mirrors the `source_book` index). Blocks whose record has no `FivetoolsSourceKey`
(homebrew) get `source_key = null` and are simply unmatched by key-scoped queries — acceptable
(scoped features target official books).

### D2 — `BookCatalog` single source of truth

`Features/Retrieval/BookCatalog.cs`: an immutable list of `(Key, DisplayName)` for the five known books
(`PHB`/`Monster Manual 2014` etc. — display names copied VERBATIM from the live facet; DMG display name
`"Dungeon Master's Guide 2014"`). Exposes `Keys`, `DisplayNames`, `DisplayNameToKey`, `KeyToDisplayName`.
Used by the backfill (display→key), the scope constants (keys), and the guard.

### D3 — `IVectorStoreService` gains facet + set-payload-by-book

- `Task<IReadOnlyDictionary<string,long>> GetSourceKeyCountsAsync(ct)` — Qdrant facet on `dnd_blocks`
  keyed by `source_key`. (Also `GetSourceBookCountsAsync` for the backfill's by-display-name pass and the
  reconcile.)
- `Task<long> SetSourceKeyForBookAsync(string displayName, string key, ct)` — Qdrant `set-payload`
  writing `source_key=key` to all points where `source_book==displayName`; returns count.

### D4 — Retrieval filters by `source_key`

`RetrievalQuery.SourceBooks` → `SourceKeys` (`IReadOnlyCollection<string>?`). `RagRetrievalService`'s
scope block builds the `should`/OR conditions on `QdrantPayloadFields.SourceKey`. `RuleSources.Books` →
`RuleSources.Keys = ["PHB","DMG"]`; `DowntimeSources` → `["XGE","DMG"]`; `SettingCatalog.Core` and the
per-setting lists → keys. The three callers pass keys.

### D5 — Backfill + DMG ingest + reconcile as admin operations

- `POST /admin/retrieval/backfill-source-keys` → for each `BookCatalog` entry, `SetSourceKeyForBookAsync`;
  returns per-book counts. Idempotent (re-running re-sets the same value).
- DMG ingest is the standard register → `ingest-blocks` flow (operational task), producing
  `source_key=DMG` blocks directly (no backfill needed for DMG).
- `POST /admin/books/reconcile` → `RegistryReconcileService`: facet `source_book`; for each display name
  with no `IngestionRecord`, insert one (`DisplayName`, `ChunkCount` from the facet, `EntityCount` from
  the `dnd_entities` facet, `Status=EntitiesIngested`, `Version` + `FivetoolsSourceKey` from `BookCatalog`,
  `FilePath`/`FileHash` linked best-effort by hashing on-disk PDFs). Never touches an existing record.

### D6 — Scope-health guard

`ScopeHealthCheck` hosted startup step: after the collection is ready, call `GetSourceKeyCountsAsync`; for
every key in `RuleSources ∪ DowntimeSources ∪ SettingCatalog`, log `WARNING` if its count is 0
(non-fatal). Unit test: those scope keys are all ⊆ `BookCatalog.Keys` (catches a typo at CI without the
live corpus).

## Risks / Trade-offs

- **[Mid-migration unmatched blocks]** → strict sequencing: ship D1 (ingest writes `source_key`) and run
  the D5 backfill BEFORE switching the D4 filter. Until the filter switches, scoping still uses
  `source_book`, so nothing breaks; after backfill every block has a key. Tasks ordered accordingly.
- **[DMG display name still cosmetic-fragile]** → it no longer affects *matching* (key does), only the
  citation text; a wrong display name is a cosmetic issue the smoke catches, not a silent empty.
- **[Backfill maps by display name — the very thing we distrust]** → true, but it runs ONCE against the
  four known live display names (read from the facet into `BookCatalog` verbatim), each verified by
  asserting the returned set-payload count equals that book's facet block count. A mismatch fails the op.
- **[Reconcile guesses FilePath/hash]** → best-effort; a record with an unlinked PDF is still truthful
  for display/count. Re-ingest (which needs the PDF) is out of scope here.
- **[`dnd_entities` unchanged]** → intentional; it already keys correctly. Only `dnd_blocks` changes.
