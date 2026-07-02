## 1. Corpus-statistics store (self-correcting, combine (1)+(2))

- [x] 1.1 Add EF entities + migration: global `Bm25TermStat(Term PK, DocumentFrequency)` and `Bm25CorpusStat(Id=1, DocumentCount, TotalTokenLength)`, plus per-book `Bm25BookStat(FileHash PK, DocumentCount, TotalTokenLength, TermDfJson)` — migration `AddBm25CorpusStats`
- [x] 1.2 `IBm25CorpusStats` store/service: read global stats; `ApplyBookAsync` (net-delta subtract-old-then-add-new, self-correcting); `RemoveBookAsync`; `RebuildAsync` (re-sum globals from all `Bm25BookStat` rows) — all transactional
- [x] 1.3 Hook block ingestion (`BlockIngestionOrchestrator`) to compute the book's per-term df + totals and call `ApplyBookAsync` in the same flow as the upsert
- [x] 1.4 Hook book deletion (`BookDeletionService`) to call `RemoveBookAsync(fileHash)`
- [x] 1.5 Admin rebuild endpoint (`POST /admin/retrieval/bm25/rebuild-stats`) → `RebuildAsync`; added to `.http` + insomnia

## 2. BM25 reads global stats (IDF on doc side, once)

- [x] 2.1 Split `Bm25Vectorizer`: `ComputeDocVectors(texts, stats)` (tf-norm × GLOBAL idf) and `ComputeQueryVector(text)` (tf-norm only, NO idf — avoids IDF²); keep `StableIndex`/`Tokenize`
- [x] 2.2 Ingestion vectorizes docs via `ComputeDocVectors` using the global stats; removed the per-batch df/n/avgDocLen computation
- [x] 2.3 Retrieval query path (`FusedRetrievalService`, `RagRetrievalService`) builds the query vector via `ComputeQueryVector`

## 3. Tests + verify

- [x] 3.1 Test: two docs with identical text vectorized against the same populated store yield identical sparse weights
- [x] 3.2 Test: `ApplyBookAsync` is self-correcting — re-applying the same book does NOT double-count global df/totals; `RemoveBookAsync` reverts to prior; `RebuildAsync` reproduces the aggregates from per-book rows
- [x] 3.3 Test: query vector is tf-only (no idf) and doc vector carries global idf (so ranking is non-degenerate and IDF is applied once)
- [x] 3.4 `dotnet build` + `dotnet test` green (856/856, 0 warnings/0 errors)
- [x] 3.5 Re-ingest a book and confirm hybrid keyword ranking is corpus-consistent — LIVE: re-ingested PHB (5243 chunks) against the running stack; Bm25 stats populated (terms=10965, corpus_docs=5243, corpus_tokens=190076); `/retrieval/search?q=fireball` returns non-degenerate ranked results (scores 0.75/0.583/0.2). Surfaced + fixed a production-only bug (transactions vs `EnableRetryOnFailure`; commit d6e0dc9). NOTE: for FULL corpus consistency, MM + DMG should also be re-ingested (then PHB once more, since global stats grow) — mechanism proven with PHB.
