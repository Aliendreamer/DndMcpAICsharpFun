## Context

`DndChatService.StreamAsync` builds a per-request `ChatOptions { Tools = [.. toolList] }` where `toolList` is the whole MCP surface (from `IMcpToolsProvider.GetToolsAsync` + per-user in-process tools) minus `search_web` when web search is off. qwen3:8b (the chat model, an MCP client) then selects tools. There is no routing step — the model chooses over ~15 tools every turn, and small-model tool selection is the documented weak point (misroutes to retrieval, flaky multi-param binding). The deterministic mechanisms the query *should* hit (`CharacterResolutionService`, structured `search_entities`, prose RAG) all exist and work; the gap is dispatch.

This is Slice 1 of the read-path router (archived `prose-grounded-knowledge-model` §H/§I). It mirrors the extraction pipeline's **classifier-as-prior** (`deterministic-type-resolution`): a cheap classifier proposes, a safe fallback guarantees a bad guess never hard-fails.

## Goals / Non-Goals

**Goals:**

- Each chat query is offered the tool **group** that matches its mechanism, shrinking the 8B's decision surface so the right tool is picked more often.
- Never strand the model: a wrong/uncertain classification degrades to the full tool set (today's behavior), never to a dead end.
- Routing decisions are logged/measured so exemplars + threshold are tunable from real usage.

**Non-Goals:**

- No new query *capability* — Slice 2 (Bin-B set-return aggregation/join) is deferred. This change routes to the existing `search_entities`; it does not make prose-RAG-unanswerable set queries answerable.
- No change to the LLM turn, tool execution, streaming, or the MCP server surface.
- No re-extraction / structured-data work (the router degrades gracefully over thin structured data).

## Decisions

**D1 — Router shapes `ChatOptions.Tools`, nothing else.** A `QueryRouter.Route(query, availableTools) → IReadOnlyList<tool>` called in `DndChatService` before `ChatOptions` is built. Pure function of (query, tool list, options); no I/O except the embedding backstop. Keeps the change surgical and the LLM path unchanged.

**D2 — Tool groups as a static name→group map.** Groups: `retrieval-lore` (fused search, `ask_setting_lore`, `ask_rules`), `structured-lookup` (`search_entities`, `get_entity`), `character-resolution` (`resolve_character_*`), `calculators` (`calculate_crafting`), `generation` (`generate_npc`, `generate_npc_party`, `prep_session`, `plan_downtime`, build/level-up/critique tools). A tool absent from the map is **always-offered** — new tools are safe by default and never hidden by an out-of-date map.

**D3 — Hybrid classifier: signals first, embedding backstop.**
- *Signal pass* (deterministic, high precision, ~µs): possessive/character-referential (`\bmy\b`, `\bI\b`, "for my character", possessive + "at level N") → `character-resolution`; set/quantifier (`\ball\b`, `\bevery\b`, "list", "which … can", "how many") → `structured-lookup`; imperative-create ("generate", "make (me)? an npc", "prep (a )?session") → `generation`. A hit = confidence 1.0.
- *Embedding backstop* (only when no signal fires): embed the query via the existing `IEmbeddingService` (mxbai, already used for retrieval, ~free) and cosine-compare to per-group **exemplar centroids** (5–8 seed phrases per group, precomputed once and cached). Argmax group; max cosine = confidence.

**D4 — Threshold gates narrow-vs-full.** `confidence ≥ Threshold` (config, default e.g. 0.45 for the embedding path; signals are always ≥ threshold) → offer `groupTools ∪ AlwaysSafeCore`; else → offer **all** tools. Threshold + exemplars are config so they tune without a code change.

**D5 — Always-safe core.** Every narrowed set includes an always-present core (the fused prose-search tool). Prose RAG can partially answer nearly anything, so the model is never stranded even if the group is wrong. This is the single most important guardrail: the router may only *remove redundant* tools when confident, never remove the safe fallback.

**D6 — Observability = tunability.** Each decision logs/meters `{group, confidence, offeredCount, totalCount, path: signal|embedding|fallback}`. Misroutes are audited from logs; exemplars/threshold refined over time — the classifier is a learnable prior, not a frozen authority (same philosophy as extraction §F).

*Alternatives rejected:* (A) persona/tool-description tuning only — already tried, doesn't shrink the surface; (C) deterministic dispatch that bypasses the LLM — a misclassification goes straight to the wrong mechanism with no LLM correction and fights the MCP-client architecture.

## Risks / Trade-offs

- **Over-narrowing hides a needed tool** → Mitigation: D5 always-safe core + D4 full-set fallback on low confidence + D2 unknown-tool-always-offered. The router can only prune redundancy under confidence.
- **Embedding latency per query** → Mitigation: signals short-circuit the common cases; the backstop is one short-text embed on the same mxbai path retrieval already uses (~ms–100ms); exemplar centroids are precomputed/cached.
- **Multi-intent queries** ("describe my Red Dragonborn and list its resistances") → Mitigation: below-threshold confidence naturally falls to the full set; a future refinement can offer the union of the top-2 groups (noted, not built).
- **Group map drift as tools are added** → Mitigation: unknown → always-offered (safe, not silently dropped); a test asserts every registered chat tool is either mapped or intentionally always-on.
