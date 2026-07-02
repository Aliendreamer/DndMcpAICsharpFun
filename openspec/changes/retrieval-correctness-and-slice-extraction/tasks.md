## 1. BM25 correctness

- [ ] 1.1 Replace `GetHashCode` term indexing with a stable hash (FNV-1a/xxHash over UTF-8); add golden-value test (COR-16, COR-17, `Features/Ingestion/Bm25Vectorizer.cs:62,69`)
- [ ] 1.2 Compute IDF/avgDocLen from persisted corpus-wide statistics, reused at query time (COR-15, `Bm25Vectorizer.cs:40`)
- [ ] 1.3 Preserve alphanumeric tokens in the tokenizer (COR-14, `Bm25Vectorizer.cs:9`)

## 2. Shared sparse-search kernel

- [ ] 2.1 Move `SparseVector` + `Bm25Vectorizer` to a feature-neutral module (STR-14, STR-15)
- [ ] 2.2 Update references in `Features/Ingestion`, `Features/VectorStore`, `Features/Retrieval`, `Infrastructure/Qdrant`; confirm no cross-slice / upward deps remain

## 3. Retrieval-slice cleanup

- [ ] 3.1 Delete dead `IReranker.SelectTopN` + unused `RerankerOptions.TopN` (SIM-17, SIM-18)
- [ ] 3.2 De-duplicate public/diagnostic endpoint scaffolding (SIM-19, `Features/Retrieval/RetrievalEndpoints.cs`)
- [ ] 3.3 Move reranker DI into `AddRetrieval()` (STR-17, `Program.cs:62`)
- [ ] 3.4 Use `IHttpClientFactory` in `ModelDownloader`; log failures with exception; remove `= default!` reliance (NET-08, NET-09, NET-10)
- [ ] 3.5 Replace real-TCP failure test with an injected handler double (COR-10)

## 4. Verify + close

- [ ] 4.1 `dotnet build` + `dotnet test` green
- [ ] 4.2 Re-ingest a book end-to-end and confirm hybrid keyword recall is non-degenerate after a restart
- [ ] 4.3 Confirm each finding (COR-14/15/16/17, STR-14/15/17, SIM-17/18/19, NET-08/09/10, COR-10) is addressed
