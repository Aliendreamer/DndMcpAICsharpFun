## Context

Structured entities live in the `dnd_entities` Qdrant collection, one point per canonical
`EntityEnvelope`. The point ID is a UUIDv5 of the entity's canonical `Id`, so re-ingesting a
book overwrites in place and **exact-ID duplicates cannot coexist**. However, the same
real-world entity enters the corpus under *different* IDs from different sources:

- A parser-produced `phb14.spell.fireball` and a 5etools-backfilled Fireball (`DataSource =
  5etools-backfill`) in the same edition.
- A 2014 spell reprinted in two 2014 books, each yielding a book-slug-prefixed ID.

`CanonicalValidationService` already flags *exact-ID collisions across files* as failures but
never resolves them, and the in-extraction `ExtractionCandidateDeduplicator` dedups only
*within a single book's extraction*. Nothing dedups the same entity across books/sources
corpus-wide, so `FusedRetrievalService` surfaces several copies of one entity in a single
result set.

Constraints: canonical JSON files under `books/canonical/` are the hand-correctable source of
truth and must not be rewritten by an automated pass. Dedup must stay out of the extraction
and ingestion write paths (roadmap Item 4).

## Goals / Non-Goals

**Goals:**

- Collapse same-entity-different-ID duplicates so retrieval returns each entity once per
  edition, driven by a deterministic authority-first winner rule.
- Make duplication *visible* (read-only report) and *reclaimable* (opt-in destructive
  compact of the Qdrant store).
- Keep the winner rule a pure, table-testable unit independent of Qdrant.

**Non-Goals:**

- Merging across editions (2014 vs 2024 stay distinct — edition is part of the key).
- Rewriting or deleting entities from canonical JSON files.
- Deduping prose (`dnd_blocks`); only the entity channel is affected.
- Fuzzy/semantic dedup of *different* entities with similar names — the key is exact
  normalized-name + type + edition.
- Enforcing dedup at ingestion time (compact is deliberately transient).

## Decisions

### Dedup key = `(EntityNameIndex.Normalize(name), Type, Edition)`

`EntityNameIndex.Normalize` (lowercase, letters+digits only) is reused purely as the
name-normalization function, computed from each entity's own fields — so homebrew entities
not present in the 5etools index dedup correctly too. Edition is part of the key, guaranteeing
2014/2024 never merge. *Alternative considered:* keying on 5etools index lookup — rejected
because it excludes homebrew and couples dedup to the external corpus.

### `DuplicateResolver` — authority-first precedence

A pure comparator over an `EntityEnvelope` group returns the single winner, in order:

1. `BookType` authority: `Core > Supplement > Adventure > Setting > Unknown`.
2. `DataSource` authority: authoritative (`5etools-backfill`, hand-authored) beats raw
   LLM-parsed.
3. Quality gate: not-`NeedsReview` beats `NeedsReview`.
4. Richness: longer `CanonicalText`.
5. Deterministic tiebreak: lexicographically smallest `Id`.

Authority leads the quality gate (user decision): a Core-book/authoritative entity wins even
if flagged for review, because source trust outranks the flag. Step 5 guarantees a stable,
reproducible winner regardless of input order or Qdrant scroll order. *Alternative
considered:* quality-gate-first ordering — rejected per the winner-rule decision.

### `BookType` is resolved at dedup time, not stored on the entity

`EntityEnvelope` carries `SourceBook` and `Edition` but **not** `BookType`, and the entity
payload writer does not persist it. Rather than add a payload field and re-ingest the whole
corpus (which would touch the write path), the caller SHALL build a `SourceBook → BookType`
lookup from the ingestion records (book registry) and pass it to `DuplicateResolver`. This
keeps the resolver pure (no I/O) and dedup fully out of the ingestion write path. Unknown /
unmapped source books resolve to `BookType.Unknown` (lowest authority). *Alternative
considered:* persisting `BookType` onto every entity point — rejected as an unnecessary
write-path change requiring a full re-ingest.

### Slice 1 — query-time collapse in `FusedRetrievalService`

After the entity candidate pool is fetched from `dnd_entities` and **before** fusion and
reranking, group the entity hits by dedup key and emit one representative per group: the
`DuplicateResolver` winner's envelope, carrying the **maximum similarity score** in the group.
Max-score (not the winner's own score) prevents a duplicate from losing rank merely because
its most-authoritative source matched the query slightly worse. Prose candidates pass through
unchanged. This is the durable correctness layer — retrieval is correct regardless of store
state. *Alternative considered:* collapsing after rerank — rejected because rerank's top-N
truncation could drop distinct entities in favour of duplicates before they're collapsed.

### Slice 2 — read-only report + destructive compact (Qdrant-only)

`IEntityVectorStore` gains a full-corpus enumeration (scroll all points) so both endpoints can
group the entire collection by dedup key:

- `GET /admin/retrieval/entities/duplicates` — pure diagnostics: every group with >1 member as
  `{ key, winnerId, loserIds[] }`.
- `POST /admin/retrieval/entities/compact` — dry-run by default (same report shape);
  `?apply=true` deletes only the loser points from Qdrant via delete-by-entity-ID. Canonical
  JSON is untouched.

Both require `X-Admin-Api-Key` (they sit under `/admin`). Compact reuses the same grouping +
`DuplicateResolver` as the report, so the dry-run output exactly predicts the apply.

## Risks / Trade-offs

- **Compact is transient** → re-ingesting a book re-adds its entities from canonical, so losers
  reappear. *Mitigation:* Slice 1 (query-time collapse) is the continuous correctness
  guarantee; compact is documented as occasional hygiene, not a permanent state. This is an
  accepted consequence of keeping dedup out of the write path.
- **Winner may not be the best semantic match to the query** → we return the authority winner,
  not the closest-scoring duplicate. *Mitigation:* the representative carries the group's max
  score, so ranking is unaffected; the user still gets the entity, once, from the most
  authoritative source (intended behavior).
- **Full-corpus scroll cost** for the report/compact on large collections. *Mitigation:*
  paginated scroll (already the pattern in `GetByIdsAsync`); these are admin-only, on-demand
  operations, not hot-path.
- **Two genuinely different entities sharing a normalized name within one type+edition** would
  be wrongly collapsed. *Mitigation:* this requires an exact normalized-name collision within
  the same type and edition, which is vanishingly rare; the report makes any such case
  visible for human review before a destructive compact.
