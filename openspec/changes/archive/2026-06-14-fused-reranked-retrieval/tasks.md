## 1. Shared RerankingService (TDD)

- [x] 1.1 Write failing tests: `RerankingService.RerankAsync(query, candidates, getText, topN)` returns topN ordered by mocked cross-encoder scores; when reranking disabled, returns first topN in original order without invoking the model
- [x] 1.2 Implement `RerankingService` wrapping `CrossEncoderReranker` (RerankAsync + SelectTopN); make tests green

## 2. Tunable RerankerOptions (TDD)

- [x] 2.1 Write failing tests: defaults (`Enabled=true`, `RerankBlocks=true`, `RerankEntities=true`, `CandidatePoolSize=20`); `Enabled=false` disables both channels; bind from the `Reranker` config section
- [x] 2.2 Add `RerankBlocks`, `RerankEntities`, `CandidatePoolSize` to `RerankerOptions`; make tests green

## 3. Entity-search reranking (TDD)

- [x] 3.1 Write failing tests for `EntityRetrievalService.SearchAsync`: with `RerankEntities=true` it over-fetches `CandidatePoolSize` and returns top-K by rerank score (mocked); with `RerankEntities=false` it returns top-K by vector score and does not call the reranker
- [x] 3.2 Update `EntityRetrievalService.SearchAsync` to use `RerankingService` over `canonicalText`; make tests green

## 4. Refactor RagRetrievalService to shared service (TDD/regression)

- [x] 4.1 Add a regression test pinning current prose selection order for a fixed candidate set + scores
- [x] 4.2 Replace `ApplyRerankerAsync` internals with `RerankingService`; keep `RerankBlocks` gating; regression test stays green

## 5. Fused retrieval + search_dnd tool (TDD)

- [x] 5.1 Write failing tests: `FusedRetrievalService` embeds once, pulls pools from both stores, builds source-tagged `FusedCandidate`s, reranks the union via `RerankingService`, returns merged top-K; each result is `source`-tagged; respects topK
- [x] 5.2 Implement `FusedRetrievalService` (inject RAG store/service + entity store + embeddings + `RerankingService`); make tests green
- [x] 5.3 Add `search_dnd(query, topK)` to `DndMcpTools` returning the merged source-tagged list; existing tools unchanged; add a tool-test asserting mixed/tagged results

## 6. Build, suite, contracts

- [x] 6.1 `dotnet build` clean (0 warnings) and `dotnet test` fully green (existing retrieval/reranker tests included)
- [x] 6.2 If a parallel `/retrieval` HTTP endpoint is exposed for fused search, update `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json`

## 7. Verify against stack

- [x] 7.1 Confirm `search_entities` now returns reranked results and `search_dnd` returns a sensible mixed list for a query that spans rules-text + a specific entity (e.g. a spell or monster), with correct `source` tags; toggle `RerankEntities=false` and confirm fallback
