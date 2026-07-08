## Why

The same real-world entity is ingested into `dnd_entities` from multiple sources under
different canonical IDs — e.g. a parser-produced `Fireball` and a 5etools-backfilled
`Fireball`, both in the 2014 edition, or a 2014 spell reprinted across two books. These
near-duplicates survive into retrieval, so the companion sees the *same* spell/monster
several times in one result set, crowding out distinct results and muddying reasoning
about "what options exist". Exact-ID duplicates already collapse (the Qdrant point ID is a
UUIDv5 of the entity ID), but same-entity-different-ID duplicates do not.

## What Changes

- Introduce a **dedup key** = `(EntityNameIndex.Normalize(name), Type, Edition)`. Two
  entities are duplicates iff their keys match but their canonical `Id`s differ. Editions
  never merge (2014 vs 2024 are genuinely different rules).
- Add a pure **`DuplicateResolver`** that picks the single winner of a duplicate group by
  authority-first precedence: (1) `BookType` authority `Core > Supplement > Adventure >
  Setting > Unknown`; (2) `DataSource` authority (`5etools-backfill`/hand-authored beats
  raw LLM-parsed); (3) not-`NeedsReview` beats `NeedsReview`; (4) longer `CanonicalText`;
  (5) lexicographically smallest `Id`.
- **Query-time collapse (durable correctness):** `FusedRetrievalService` groups retrieved
  entity hits by dedup key and emits one representative per group — the `DuplicateResolver`
  winner's envelope carrying the **max similarity score** in the group — before fusion and
  reranking. Prose (`dnd_blocks`) is untouched.
- **Read-only duplicate report:** `GET /admin/retrieval/entities/duplicates` scans the full
  `dnd_entities` corpus and reports every duplicate group (`key`, `winnerId`, `loserIds`).
- **Destructive compact pass:** `POST /admin/retrieval/entities/compact` (dry-run by
  default; `?apply=true` deletes only the loser points from Qdrant). Canonical JSON is
  never rewritten — the report surfaces canonical-level duplicates for human action.
- Dedup stays **out of the extraction and ingestion write path** — correctness is enforced
  at query time; compact is occasional, explicitly transient storage hygiene.
- Update `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json` for the two new endpoints.

## Capabilities

### New Capabilities

- `entity-deduplication`: the dedup key, the `DuplicateResolver` precedence, the read-only
  duplicate-report endpoint, and the destructive Qdrant-only compact endpoint.

### Modified Capabilities

- `fused-reranked-retrieval`: entity candidates are collapsed by dedup key (one
  max-score representative per group) before fusion/reranking; distinct editions and prose
  results are preserved.

## Impact

- **Code:** new `DuplicateResolver` + dedup-key helper (retrieval/entities); changes to
  `FusedRetrievalService`; new admin endpoints alongside `RetrievalAdminEndpoints` /
  `EntityRetrievalEndpoints`; `IEntityVectorStore` gains a full-corpus scroll + delete-by-id
  path for compact.
- **APIs:** two new admin endpoints (both require `X-Admin-Api-Key`). No breaking changes to
  existing routes; existing retrieval responses simply contain fewer duplicate entities.
- **Data:** compact deletes loser points from `dnd_entities` only; re-ingesting a book
  re-adds them (transient by design). No schema or canonical-file changes.
- **Docs:** `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json` (per the http-contracts
  sync rule).
