## Why

The audit's COR-15 finding is split out of `retrieval-correctness-and-slice-extraction` because a
correct fix is a small feature, not a patch. `Bm25Vectorizer.ComputeBatch` derives document frequency,
corpus size, and average document length from **only the documents in the current call** (batches of
32 at ingestion, or a single doc at query time). IDF is meant to be corpus-global: identical text in
two ingest batches gets different sparse weights, and a single-document query batch always yields
`df=1, n=1 → idf≈1` for every term, erasing IDF weighting entirely. Hybrid ranking is therefore
inconsistent between ingestion and query.

This change adds a persisted corpus-statistics store that BM25 reads for globally-consistent IDF.
(The deterministic sparse-index hashing — COR-16/17 — already shipped separately; this change only
addresses the statistics.)

Closes audit finding: **COR-15**.

## What Changes

- **Corpus-statistics store:** a persisted table of per-term document frequency plus the corpus totals
  (document count, average document length), updated incrementally during block ingestion.
- **BM25 reads global stats:** `Bm25Vectorizer` (or a stats-aware wrapper) computes IDF and length
  normalization from the persisted corpus statistics at both ingestion and query time, rather than
  per batch.
- **Re-ingestion:** existing collections must be re-ingested so all sparse weights derive from the
  same global statistics.

## Capabilities

### Modified Capabilities

- `retrieval-vector-search`: adds the requirement that BM25 term statistics are corpus-global and
  persisted, superseding the per-batch computation.

## Impact

- New: a corpus-statistics EF entity + migration; incremental update on ingestion; a read path at
  query time.
- Modified: `Features/Ingestion/Bm25Vectorizer.cs` (or a stats-aware caller),
  `BlockIngestionOrchestrator`, the retrieval query path.
- Requires re-ingestion of `dnd_blocks` for consistent sparse weights.

## Non-goals

- Changing the deterministic term→index hashing (already shipped as COR-16/17).
- Dense-embedding or reranking changes.
