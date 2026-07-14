# stable-book-key-retrieval Specification

## Purpose
TBD - created by archiving change stable-book-key-retrieval. Update Purpose after archive.
## Requirements
### Requirement: Blocks carry a stable source key

Every block ingested into `dnd_blocks` SHALL carry a `source_key` payload field derived from its
`IngestionRecord.FivetoolsSourceKey`, in addition to the `source_book` display name. The `dnd_blocks`
collection SHALL have a keyword payload index on `source_key`.

#### Scenario: Ingested block gets the record's key

- **WHEN** a book with `FivetoolsSourceKey="DMG"` is ingested via `ingest-blocks`
- **THEN** each resulting block's payload has `source_key="DMG"` and `source_book="Dungeon Master's Guide 2014"`

#### Scenario: Homebrew book without a key

- **WHEN** a book with no `FivetoolsSourceKey` is ingested
- **THEN** its blocks have `source_key=null` and are not matched by key-scoped retrieval

### Requirement: BookCatalog is the single key/display-name source of truth

`BookCatalog` SHALL define, for each known book, its stable `Key` and exact `DisplayName`, and expose
lookups in both directions. The display names for already-ingested books MUST equal the values present in
the live corpus.

#### Scenario: Round-trip lookup

- **WHEN** `BookCatalog.KeyToDisplayName["DMG"]` and `BookCatalog.DisplayNameToKey["Dungeon Master's Guide 2014"]` are read
- **THEN** they return `"Dungeon Master's Guide 2014"` and `"DMG"` respectively

### Requirement: Retrieval scopes by source key

Block retrieval scoping SHALL filter on `source_key`, not the display name. `RetrievalQuery` SHALL carry
`SourceKeys`, and `RuleSources`/`DowntimeSources`/`SettingCatalog` SHALL express their scope as keys.

#### Scenario: Rules query scoped to core keys

- **WHEN** `ask_rules` retrieves with `RuleSources.Keys = ["PHB","DMG"]`
- **THEN** the Qdrant filter is an OR over `source_key ∈ {PHB, DMG}` and only PHB/DMG blocks are returned

#### Scenario: DMG content is now reachable

- **WHEN** the DMG is ingested (`source_key=DMG`) and a DMG-covered rules question is asked
- **THEN** DMG passages are retrieved and cited (previously impossible — 0 DMG blocks)

### Requirement: One-time source-key backfill of existing blocks

`POST /admin/retrieval/backfill-source-keys` SHALL set `source_key` on all existing blocks by mapping
their `source_book` display name to the `BookCatalog` key via Qdrant `set-payload`, without re-embedding.
It SHALL be idempotent and report the per-book count updated.

#### Scenario: Backfill sets keys for every known book

- **WHEN** the backfill runs against a corpus of PHB/MM/XGE/ERLW blocks
- **THEN** every block gains the `source_key` for its display name, and the reported count per book equals
  that book's block count in the facet

### Requirement: Scope-health guard warns on missing-ingest

At startup the app SHALL facet `dnd_blocks` by `source_key` and log a WARNING (non-fatal) for every scope
key (across `RuleSources`, `DowntimeSources`, `SettingCatalog`) that has zero blocks. A unit test SHALL
assert every such scope key is a member of `BookCatalog.Keys`.

#### Scenario: Missing book warns loudly instead of silently returning empty

- **WHEN** a scope key (e.g. `DMG`) has 0 blocks at startup
- **THEN** a WARNING naming that key is logged and the app still starts

#### Scenario: Scope keys are catalog members

- **WHEN** the consistency unit test runs
- **THEN** every key in `RuleSources ∪ DowntimeSources ∪ SettingCatalog` is in `BookCatalog.Keys`

### Requirement: Metadata-only registry reconcile

`POST /admin/books/reconcile` SHALL create an `IngestionRecord` for every `source_book` present in
`dnd_blocks` that has no existing record, populating `DisplayName`, `ChunkCount` (from the facet),
`EntityCount` (from the `dnd_entities` facet), `Status=EntitiesIngested`, and `Version`/`FivetoolsSourceKey`
from `BookCatalog`. It SHALL NOT modify or delete any existing record and SHALL NOT re-ingest.

#### Scenario: Orphaned books get records

- **WHEN** `dnd_blocks` has MM/XGE/ERLW blocks but `IngestionRecords` has only PHB, and reconcile runs
- **THEN** new records for MM/XGE/ERLW are created with their facet block counts and `Status=EntitiesIngested`,
  and the PHB record is untouched

#### Scenario: Reconcile is a no-op when the registry is complete

- **WHEN** reconcile runs and every `source_book` already has a record
- **THEN** no record is created or modified

