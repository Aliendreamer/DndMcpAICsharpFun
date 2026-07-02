## Why

The audit found the one **systemic correctness defect** in the retrieval pipeline: BM25 sparse-vector
indices are derived from `String.GetHashCode()`, which is randomized per process in .NET. Vectors
written to Qdrant during ingestion no longer align with query-time vectors after any restart, so
hybrid keyword retrieval silently degrades to near-zero recall — with no error. The IDF statistics are
also computed per 32-doc batch instead of over the corpus, and the tokenizer drops digits. These
share a root cause with the two verified structure findings: `SparseVector` and `Bm25Vectorizer` live
inside `Features/Ingestion`, so `Features/VectorStore`, `Features/Retrieval`, and even
`Infrastructure/Qdrant` all reach upward into the ingestion slice to use them. Fixing the hashing and
extracting the sparse primitives into a shared kernel is one coherent piece of work.

Closes audit findings: **COR-14, COR-15, COR-16, COR-17, STR-14, STR-15, STR-17, SIM-17, SIM-18,
SIM-19, NET-08, NET-09, NET-10, COR-10**.

## What Changes

- **Deterministic BM25 hashing (COR-16, COR-17):** replace `Math.Abs(term.GetHashCode()) % VocabSize`
  with a stable hash (FNV-1a / xxHash over UTF-8 bytes) so term→index is identical across processes;
  add a golden-value test.
- **Corpus-global IDF (COR-15):** compute IDF and average document length from persisted corpus-wide
  statistics (or a fixed precomputed map), reused at query time — not per batch.
- **Digit-preserving tokenizer (COR-14):** include alphanumerics so numeric keyword terms survive.
- **Shared sparse-search kernel (STR-14, STR-15):** move `SparseVector` and `Bm25Vectorizer` out of
  `Features/Ingestion` into a feature-neutral module that Ingestion, VectorStore, Retrieval, and
  `Infrastructure/Qdrant` all depend on, restoring the dependency direction.
- **Retrieval-slice cleanup (SIM-17, SIM-18, SIM-19, STR-17, NET-08, NET-09, NET-10):** drop the dead
  `IReranker.SelectTopN` and unused `RerankerOptions.TopN`, de-duplicate the public/diagnostic
  endpoint scaffolding, move reranker DI into `AddRetrieval()`, use `IHttpClientFactory` in
  `ModelDownloader`, and fix its broad exception swallowing and the `= default!` param.
- **Test hardening (COR-10):** replace the real-TCP failure test with an injected handler double.

## Capabilities

### New Capabilities

- `retrieval-vector-search`: the correctness and structural contract of the hybrid (dense + BM25
  sparse) retrieval path — deterministic sparse indexing, corpus-global term statistics, and the
  shared sparse-search primitives that ingestion, vector store, retrieval, and the Qdrant client all
  consume.

### Modified Capabilities

<!-- None; this is the first spec to formalize the retrieval-vector-search contract. -->

## Impact

- Moved: `SparseVector`, `Bm25Vectorizer` → shared kernel (new namespace); references updated in
  `Features/Ingestion`, `Features/VectorStore`, `Features/Retrieval`, `Infrastructure/Qdrant`.
- Modified: `Bm25Vectorizer` (hashing, tokenizer), a new corpus-statistics store, retrieval endpoint
  wiring, `ModelDownloader`, `IReranker`/`RerankerOptions`.
- **Re-ingestion required:** existing Qdrant sparse vectors were written with the old hashing;
  after this change the collection SHALL be re-ingested so sparse indices are consistent.
- New/updated tests: golden sparse-index value, corpus-IDF, digit tokenization, reranker path.

## Non-goals

- Changing the dense embedding model or the fusion/reranking algorithm itself.
- Migrating Qdrant collection schema beyond what the sparse-index fix requires.
