## Why

The companion can't answer **set** queries — "all CR-5 flyers", "every level-3 fire spell". Prose RAG returns passages, and `search_entities` returns the top-K entities by *vector similarity*, so neither yields a **complete, deterministic set**. The `chat-query-router` (shipped) routes these queries to the `structured-lookup` group, but that group has no complete-set tool. This is Slice 2 of the read-path router (archived `prose-grounded-knowledge-model` §H/§I, "Bin B — filter/aggregate/join → structured query that returns sets"). Key enabler: Qdrant already supports complete filter-only retrieval (`ScrollAsync`, used by the store today) plus `CountAsync` — so a filter-set is `Count(filter)` + `Scroll(filter, cap)`, complete and deterministic, over the entity fields already indexed in `dnd_entities`. No new storage substrate needed.

## What Changes

- Add a filter-only complete-set retrieval path: a store `ListByFilterAsync`, a service `ListAsync`, and compact result DTOs.
- Add a `list_entities` MCP tool (the complete set matching structured filters), keeping `search_entities` as the semantic top-K tool.
- Add a public `GET /retrieval/entities/list` endpoint (parity with `/retrieval/entities/search`), with the `.http`/`.insomnia` contract updated in the same commit.
- Register `list_entities` in the router's `structured-lookup` group so the existing "all/every/how many" signal offers it.

## Capabilities

### New Capabilities

- `entity-set-query`: complete, deterministic filter-set retrieval over the indexed `dnd_entities` fields (type, CR range, spell level, damage type, keyword, source book, SRD flags), returning compact rows with an honest total-vs-returned count — never top-K-by-similarity, never silent truncation.

### Modified Capabilities

<!-- none: additive. Existing entity-search / router requirements are unchanged; this adds a parallel complete-set path. -->

## Impact

- `IEntityVectorStore` + `QdrantEntityVectorStore` — new `ListByFilterAsync` (Count + Scroll, filter-only, max-scan guard).
- `EntityRetrievalService` + new `EntitySetResult` / `EntitySetRow` DTOs.
- `Features/Mcp/DndMcpTools.cs` — new `list_entities` tool (`search_entities` untouched).
- `Features/Retrieval/Entities/EntityRetrievalEndpoints.cs` — new `GET /retrieval/entities/list`.
- `Features/Chat/Routing/ToolGroups.cs` — add `list_entities` to `structured-lookup`.
- `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json` — the new endpoint.
- **Out of scope (later slices):** JOINs (spell↔class) and multi-attribute aggregation over the entity `fields` JSON — both need relationship modeling / a richer substrate.
