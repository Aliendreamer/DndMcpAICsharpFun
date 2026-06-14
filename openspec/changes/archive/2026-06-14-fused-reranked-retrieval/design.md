## Context

`CrossEncoderReranker` exposes `RerankAsync(query, passages)` and `SelectTopN(candidates, scores, n)`, plus `Enabled`. `RagRetrievalService.ApplyRerankerAsync` already does over-fetch → `RerankAsync` → `SelectTopN` for prose. `EntityRetrievalService.SearchAsync` embeds the query and calls `store.SearchAsync(vector, filters, topK)` with no rerank. The entity store returns hits whose payload includes `canonicalText` (what was embedded). `DndMcpTools` wires `search_lore` → `ragService`, `search_entities` → `entityService`, `get_entity` → entity by id. `RerankerOptions` binds the `Reranker` config section.

## Goals / Non-Goals

**Goals:**
- One reranking code path; both channels reranked; per-channel tunability.
- A fused tool returning the single best context across prose + entities.

**Non-Goals:**
- Removing or renaming the existing tools (additive).
- Chat system-prompt changes to use the fused tool (spec #4).
- A new embedding model or score-fusion math beyond cross-encoder reranking of the union.

## Decisions

**1. `RerankingService` (shared).** New service with `Task<IReadOnlyList<T>> RerankAsync<T>(string query, IReadOnlyList<T> candidates, Func<T,string> getText, int finalTopN, CancellationToken ct)`. It is generic so prose, entity, and fused callers reuse it. When the reranker is disabled (globally, per-channel, or model missing) it returns the first `finalTopN` candidates unchanged (stable fallback). It wraps `CrossEncoderReranker.RerankAsync` + `SelectTopN`.

**2. Entity reranking.** `EntityRetrievalService.SearchAsync` over-fetches `CandidatePoolSize` hits (when `RerankEntities` is on) instead of `topK`, then `RerankingService.RerankAsync(query, hits, h => h.CanonicalText, q.TopK)`. With `RerankEntities` off it fetches `topK` and skips reranking (today's behavior).

**3. Config (`RerankerOptions`).** Add `RerankBlocks` (default true), `RerankEntities` (default true), `CandidatePoolSize` (default 20). `Enabled` remains the global kill-switch (false disables both channels). The per-channel flags gate over-fetch + rerank in each service; `CandidatePoolSize` replaces the hard-coded pool. Keep `ModelPath`/`ModelUrl`/graceful-disable.

**4. Fused retrieval.** A `FusedRetrievalService` (or a method on a retrieval coordinator): embed the query once; fetch a candidate pool from `dnd_blocks` (via the RAG store) and from `dnd_entities` (via the entity store); wrap each candidate in a `FusedCandidate { Source ("prose"|"entity"), Id/Ref, Title, Text, Payload }` where `Text` is the block text or the entity `canonicalText`; rerank the **combined** list via `RerankingService.RerankAsync(query, all, c => c.Text, topK)`; return the merged top-K. Source-tagging is preserved end to end so the caller knows each item's origin.

**5. `search_dnd` MCP tool.** New tool on `DndMcpTools`: `search_dnd(query, topK)` → calls `FusedRetrievalService`, returns the merged list with each result carrying its `source`, identifier, title/snippet, and score. Existing tools untouched. Tool description tells the model it returns the best mix of rules-text and structured entities for a query.

**6. Pool sizing for fusion.** Each channel contributes up to `CandidatePoolSize` candidates to the union before reranking, so the cross-encoder chooses the final top-K across roughly `2 × CandidatePoolSize` items. Acceptable cost at this corpus size; revisit if latency matters.

## Risks / Trade-offs

- **Latency.** Reranking entity search and the fused union adds cross-encoder passes (CPU/ONNX). Mitigated by `CandidatePoolSize` and per-channel toggles; the reranker already runs for prose without issue. Fused tool reranks ~2× the pool — bounded and configurable.
- **Heterogeneous candidate text.** Prose chunks and entity `canonicalText` differ in length/shape; the cross-encoder scores both as (query, passage) and that's exactly its job. No normalization needed; if one source systematically dominates, the per-channel pool sizes are the tuning lever (future).
- **Generic `RerankingService` over existing call sites.** Refactoring `RagRetrievalService` to use it must preserve current behavior — covered by a regression test asserting identical selection for the prose path.
- **Tool proliferation.** Adding `search_dnd` alongside three tools gives the LLM four retrieval tools. Acceptable; spec #4 will steer the model to prefer `search_dnd`. We deliberately do not remove the specific tools (targeted lookups still valuable).
