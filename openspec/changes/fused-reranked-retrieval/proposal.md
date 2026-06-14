## Why

The cross-encoder reranker (`CrossEncoderReranker`, ms-marco-MiniLM, already downloaded) reranks the prose RAG path (`search_lore` → `dnd_blocks`), but the structured entity path (`search_entities` → `dnd_entities`) does a plain vector search with **no reranking**. The chat uses both tools, so half its retrieved context is unranked. There's also no way to retrieve the single best context across both channels at once — the LLM must call two tools and reconcile the results itself.

## What Changes

- Extract the over-fetch→rerank→select pattern (inline in `RagRetrievalService`) into one shared `RerankingService` used by every retrieval path.
- Apply reranking to **entity search**: over-fetch a candidate pool from `dnd_entities`, rerank by each entity's `canonicalText`, return top-K.
- Make reranking **tunable**: per-channel toggles (`RerankBlocks`, `RerankEntities`) and a configurable `CandidatePoolSize`, plus the existing global `Enabled`.
- Add a **fused unified MCP tool** `search_dnd`: query both `dnd_blocks` and `dnd_entities`, jointly rerank the combined candidate set with the cross-encoder, and return one merged top-K list where each result is tagged by `source` (`prose` vs `entity`). The existing `search_lore` / `search_entities` / `get_entity` tools remain for targeted lookups.

## Capabilities

### New Capabilities

- `fused-reranked-retrieval`: a shared reranking service, reranked entity search, per-channel/pool-size config, and a fused cross-channel retrieval that jointly reranks prose + entities into one source-tagged ranked list.

### Modified Capabilities

- `mcp-retrieval-tools`: add the `search_dnd` fused tool (existing `search_lore` / `search_entities` / `get_entity` unchanged).

## Impact

- Code: new `RerankingService` (wraps `CrossEncoderReranker`); `EntityRetrievalService.SearchAsync` reranks via it; `RerankerOptions` gains `RerankBlocks`/`RerankEntities`/`CandidatePoolSize`; new fused retrieval orchestration querying both stores; new `search_dnd` MCP tool in `DndMcpTools`. `RagRetrievalService` refactored to use the shared service (no behavior change).
- Config: `Reranker` section gains the new knobs (sensible defaults: reranking on for both channels, pool 20).
- Contracts: `search_dnd` is an MCP tool; if a parallel `/retrieval` HTTP endpoint is added, update `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json`.
- Out of scope: the chat system prompt actually *preferring* the fused tool / response style — that is spec #4 (conversational responses).
