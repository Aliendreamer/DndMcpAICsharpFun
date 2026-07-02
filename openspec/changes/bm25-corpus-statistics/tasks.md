## 1. Corpus-statistics store

- [ ] 1.1 Add a corpus-statistics EF entity (per-term document frequency + corpus totals) and a migration
- [ ] 1.2 Update the statistics incrementally during block ingestion (`BlockIngestionOrchestrator`)

## 2. BM25 reads global stats

- [ ] 2.1 Compute IDF + avgDocLen from the persisted statistics in `Bm25Vectorizer` (or a stats-aware wrapper), at ingestion and query time
- [ ] 2.2 Remove the per-batch df/n/avgDocLen computation

## 3. Tests + verify

- [ ] 3.1 Test: identical text in two batches yields identical sparse weights
- [ ] 3.2 Test: single-term query IDF comes from corpus stats (not degenerate)
- [ ] 3.3 `dotnet build` + `dotnet test` green
- [ ] 3.4 Re-ingest a book and confirm hybrid keyword ranking is corpus-consistent
