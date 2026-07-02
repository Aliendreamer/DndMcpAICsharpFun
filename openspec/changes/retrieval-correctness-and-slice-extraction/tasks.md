## 1. BM25 correctness

- [x] 1.1 Replace `GetHashCode` term indexing with a stable hash (FNV-1a/xxHash over UTF-8); add golden-value test (COR-16, COR-17, `Features/Ingestion/Bm25Vectorizer.cs:62,69`)
- [x] 1.2 SPLIT OUT — COR-15 (corpus-global IDF) moved to its own change `bm25-corpus-statistics` (needs a persisted corpus-statistics store). Tracked in the roadmap.
- [x] 1.3 Preserve alphanumeric tokens in the tokenizer (COR-14, `Bm25Vectorizer.cs:9`)

## 2. Shared sparse-search kernel

- [x] 2.1 Move `SparseVector` + `Bm25Vectorizer` to a feature-neutral module (STR-14, STR-15) — now `Infrastructure/Search` (`DndMcpAICsharpFun.Infrastructure.Search`)
- [x] 2.2 Update references in `Features/Ingestion`, `Features/VectorStore`, `Features/Retrieval`, `Infrastructure/Qdrant`; confirm no cross-slice / upward deps remain (Qdrant `DomainSparseVector` aliases now point downward)

## 3. Retrieval-slice cleanup

- [x] 3.1 Delete dead `IReranker.SelectTopN` + unused `RerankerOptions.TopN` (SIM-17, SIM-18)
- [x] 3.2 De-duplicate public/diagnostic endpoint scaffolding (SIM-19, `Features/Retrieval/RetrievalEndpoints.cs`) — `[AsParameters] SearchRequest` record struct
- [x] 3.3 Move reranker DI into `AddRetrieval()` (STR-17, `Program.cs:62`)
- [x] 3.4 Use `IHttpClientFactory` in `ModelDownloader`; log failures with exception; remove `= default!` reliance (NET-08, NET-09, NET-10)
- [x] 3.5 Replace real-TCP failure test with an injected handler double (COR-10)

## 4. Verify + close

- [x] 4.1 `dotnet build` + `dotnet test` green (845/845, 0 warnings/0 errors, sandbox-disabled per git-crypt)
- [x] 4.2 Re-ingest a book end-to-end and confirm hybrid keyword recall is non-degenerate after a restart — LIVE: PHB re-ingested (5243 chunks) after an app rebuild/restart; `/retrieval/search?q=fireball` returns non-degenerate hybrid results (varied scores 0.75/0.583/0.2), confirming the deterministic-hash + global-IDF sparse path works end-to-end
- [x] 4.3 Confirm each finding (COR-14/16/17, STR-14/15/17, SIM-17/18/19, NET-08/09/10, COR-10) is addressed — COR-15 split to `bm25-corpus-statistics`
