# Design — bm25-corpus-statistics (COR-15)

## Problem (precise)

`Bm25Vectorizer.ComputeBatch` derives `df` (document frequency), `n` (corpus size), and `avgDocLen`
from **only the documents in the current call**:

- **Ingestion** vectorizes in 32-doc batches, so a term's IDF is computed from a 32-doc sample. The
  same text in two different batches gets **different** sparse weights — ranking is batch-dependent.
- **Query** vectorizes a single-doc "batch", so every term gets `df=1, n=1 → idf≈1`.

Today IDF effectively lives on the **doc** side only (the query's `idf≈1` makes the query vector ~flat).
The Qdrant sparse score is `Σ query_weight[i] · doc_weight[i]`, so IDF is applied once (on the doc). That
part is not wrong — the defect is that the doc-side IDF is computed **per batch**, not **corpus-global**.

### Correctness trap to avoid
Naively "making the query read global IDF too" would put IDF on **both** vectors → the score carries
**IDF²**, over-weighting rare terms. So the fix must keep IDF applied **once**.

## Decision

Keep IDF on the **doc** side, applied once, but source `df / n / avgDocLen` from a **persisted
corpus-global statistics store** instead of the current call's batch. The **query** vector stays
tf-normalized with **no IDF** (a `ComputeQueryVector` path), which is the consistent BM25-in-sparse-vectors
scheme and avoids IDF².

### Store (Postgres, via EF + migration)

Global aggregates (what BM25 reads):

- `Bm25TermStat { string Term (PK), long DocumentFrequency }` — per-term df across the whole `dnd_blocks`
  corpus.
- `Bm25CorpusStat { int Id = 1 (singleton row), long DocumentCount, long TotalTokenLength }` — corpus
  totals; `avgDocLen = TotalTokenLength / max(DocumentCount, 1)`.

Per-book contribution (enables exact self-correction on re-ingest/delete — the "combine (1)+(2)" choice):

- `Bm25BookStat { string FileHash (PK), long DocumentCount, long TotalTokenLength, string TermDfJson }` —
  ONE row per ingested book keyed by its `FileHash`. `TermDfJson` is `{ term: dfInThisBook }` (the number
  of that book's docs containing the term). The global aggregates are exactly the sum of all `Bm25BookStat`
  rows.

### Update rules (self-correcting)

All wrapped in a transaction with the block upsert/delete:

- **Ingest / re-ingest a book:** compute the book's `{term→df}`, `DocumentCount`, `TotalTokenLength`. If a
  `Bm25BookStat` row already exists for this `FileHash` (a re-ingest), **subtract** its old contribution
  from the global aggregates first. Then **add** the new contribution and **upsert** the `Bm25BookStat`
  row. → re-ingesting a book never double-counts and never drifts.
- **Delete a book:** subtract its `Bm25BookStat` from the global aggregates and delete the row.
- **Rebuild op (admin, the recovery net):** clear the global aggregates and re-sum them from all
  `Bm25BookStat` rows (DB-only, authoritative — no Qdrant re-read). Fixes any drift from an interrupted
  ingest or a bug.

### Read paths

- **Ingestion** (`BlockIngestionOrchestrator`): update the store from the batch, then vectorize the
  batch's docs using the **current global** store (IDF on doc side).
- **Query** (`FusedRetrievalService`, `RagRetrievalService`): build the query sparse vector as tf-only
  (no store read needed for IDF, since IDF is on the doc side). *(If we later want query-side IDF and
  binary doc weights instead, that's the mirror scheme — but we pick doc-side IDF to minimize churn.)*

### BM25 shape change

`Bm25Vectorizer` (currently a static that computes df+idf+tf from the batch) splits into:

- `ComputeDocVectors(texts, IBm25CorpusStats stats)` — tf-norm × **global** idf (from `stats`).
- `ComputeQueryVector(text)` — tf-norm only, no idf.

A new `IBm25CorpusStats` abstraction (backed by the Postgres store) is injected where docs are vectorized.
`StableIndex`/`Tokenize` (COR-16/17, already shipped) are unchanged and shared.

## Consistency model

The STATS store is always exact: per-book contributions make ingest/re-ingest/delete self-correct, and the
rebuild op re-derives the global aggregates from the per-book rows. So a term's global `df` and the corpus
totals always reflect the current corpus, regardless of ingest order.

The one inherent BM25 caveat remains at the **vector** layer (not the stats layer): doc sparse vectors are
**baked into Qdrant at ingest time** using the global stats *as they were then*. If books are added later,
the global `df`/`n` grow, so earlier docs' baked weights lag the newest stats until those blocks are
re-ingested. This is the standard incremental-BM25 trade-off (Lucene/ES recompute norms on merge). It is
bounded and correct-on-re-ingest; the finding's invariant (identical text vectorized **against the same
stats state** → identical weights) holds, and a corpus re-ingest gives full vector-layer consistency.

**Existing `dnd_blocks` must be re-ingested once** to pick up global-stats weighting (the proposal states
this). No auto-rebuild of the whole corpus on each incremental ingest.

## Impact

- New: `Domain` stats entities + EF config + **migration**; `IBm25CorpusStats` store + repository;
  incremental update in `BlockIngestionOrchestrator`.
- Modified: `Infrastructure/Search/Bm25Vectorizer.cs` (split doc/query), the two retrieval services
  (query path → `ComputeQueryVector`), DI registration.
- Operational: re-ingest `dnd_blocks` for consistent weights.

## Non-goals

- Changing the term→index hashing (COR-16/17, shipped).
- Dense-embedding or reranking changes.
- Auto-rebuilding the whole corpus on each incremental ingest.
