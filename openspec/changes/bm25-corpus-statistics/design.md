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

- `Bm25TermStat { string Term (PK), long DocumentFrequency }` — per-term df across the whole `dnd_blocks`
  corpus.
- `Bm25CorpusStat { int Id = 1 (singleton row), long DocumentCount, long TotalTokenLength }` — corpus
  totals; `avgDocLen = TotalTokenLength / max(DocumentCount, 1)`.

Updated **incrementally** during block ingestion, in the same unit of work as the upsert: for each
ingested doc, `+1` to each **unique** term's `DocumentFrequency`, `+1` to `DocumentCount`, and
`+tokenCount` to `TotalTokenLength`. Concurrency-safe via an upsert/`ExecuteUpdate` increment.

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

## Consistency model (the honest caveat)

Because doc sparse vectors are **baked into Qdrant at ingest time**, and the global store **grows** as more
books are ingested, docs ingested earlier used a smaller `n`/`df` snapshot than docs ingested later. So:

- Identical text ingested **against the same store state** → identical weights (the invariant the finding
  wants; test 3.1 populates the store, then vectorizes both).
- Across incremental ingests, weights are **approximately** consistent and **fully** consistent after a
  **corpus re-ingest** (re-vectorize all blocks against the final store). This mirrors how Lucene/ES
  recompute norms on segment merge.

**Re-ingestion is required for existing collections** to pick up global stats (the proposal already states
this). Provided as a manual admin re-ingest (no auto-rebuild on every ingest — that would re-vectorize the
whole corpus each time).

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
