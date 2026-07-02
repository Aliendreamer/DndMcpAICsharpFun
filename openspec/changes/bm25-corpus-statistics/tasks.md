## 1. Corpus-statistics store (self-correcting, combine (1)+(2))

- [ ] 1.1 Add EF entities + migration: global `Bm25TermStat(Term PK, DocumentFrequency)` and `Bm25CorpusStat(Id=1, DocumentCount, TotalTokenLength)`, plus per-book `Bm25BookStat(FileHash PK, DocumentCount, TotalTokenLength, TermDfJson)`
- [ ] 1.2 `IBm25CorpusStats` store/service: read global stats; `ApplyBookAsync(fileHash, terms→df, docCount, totalTokens)` that subtracts any existing book contribution then adds the new one (self-correcting); `RemoveBookAsync(fileHash)`; `RebuildAsync()` (re-sum globals from all `Bm25BookStat` rows) — all transactional
- [ ] 1.3 Hook block ingestion (`BlockIngestionOrchestrator`) to compute the book's per-term df + totals and call `ApplyBookAsync` in the same flow as the upsert
- [ ] 1.4 Hook book deletion (`BookDeletionService`) to call `RemoveBookAsync(fileHash)`
- [ ] 1.5 Admin rebuild endpoint (`POST /admin/retrieval/bm25/rebuild-stats`) → `RebuildAsync`; add to `.http` + insomnia

## 2. BM25 reads global stats (IDF on doc side, once)

- [ ] 2.1 Split `Bm25Vectorizer`: `ComputeDocVectors(texts, stats)` (tf-norm × GLOBAL idf) and `ComputeQueryVector(text)` (tf-norm only, NO idf — avoids IDF²); keep `StableIndex`/`Tokenize`
- [ ] 2.2 Ingestion vectorizes docs via `ComputeDocVectors` using the global stats; remove the per-batch df/n/avgDocLen computation
- [ ] 2.3 Retrieval query path (`FusedRetrievalService`, `RagRetrievalService`) builds the query vector via `ComputeQueryVector`

## 3. Tests + verify

- [ ] 3.1 Test: two docs with identical text vectorized against the same populated store yield identical sparse weights
- [ ] 3.2 Test: `ApplyBookAsync` is self-correcting — re-applying the same book does NOT double-count global df/totals; `RemoveBookAsync` reverts to prior; `RebuildAsync` reproduces the aggregates from per-book rows
- [ ] 3.3 Test: query vector is tf-only (no idf) and doc vector carries global idf (so ranking is non-degenerate and IDF is applied once)
- [ ] 3.4 `dotnet build` + `dotnet test` green
- [ ] 3.5 Re-ingest a book and confirm hybrid keyword ranking is corpus-consistent (operational — needs the stack)
