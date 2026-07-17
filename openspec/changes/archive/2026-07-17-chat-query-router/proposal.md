## Why

The companion chat is an MCP client: `DndChatService` hands the **entire** tool suite (fused search, `search_entities`, `get_entity`, `resolve_character_*`, `ask_rules`, `ask_setting_lore`, `calculate_crafting`, the NPC/session/build tools, web search) to qwen3:8b at once, and the 8B model picks. That large surface is where routing goes wrong — the roadmap documents real misroutes (calculator queries answered by retrieval; character-resolution answered by prose reasoning instead of the deterministic engine). This is Slice 1 of the read-path query router from the archived `prose-grounded-knowledge-model` design (§H/§I): make each query hit the RIGHT mechanism by shrinking the 8B's decision surface, reusing the proven classifier-as-prior pattern from extraction.

## What Changes

- Add a `QueryRouter` service, called in `DndChatService` immediately before it builds `ChatOptions.Tools`. It tags the user's latest message to a tool **group** and offers the LLM only that group's tools plus an always-safe core; on low confidence it offers the full set (today's behavior). The router only shapes the `Tools` list — the LLM turn and tool execution are unchanged.
- Hybrid classifier: high-precision deterministic **signals** (possessive → character-resolution; set/quantifier → structured-lookup; imperative-create → generation), with an **embedding backstop** (existing mxbai `IEmbeddingService` vs per-group exemplar centroids) when no signal fires; a config **threshold** gates the narrow-vs-full decision.
- Emit a routing-decision log/metric per query so misroutes are auditable and the exemplars/threshold are tunable from real logs.

## Capabilities

### New Capabilities

- `chat-query-routing`: a pre-LLM classifier that maps a chat query to a tool group and narrows the offered tool set (with a safe full-set fallback), so the right retrieval/structured/resolution mechanism is used per query.

### Modified Capabilities

<!-- none: additive — the chat tool-dispatch gains a routing step; no existing spec requirement changes -->

## Impact

- New `Features/Chat/Routing/` — `QueryRouter`, the tool-group map, the exemplar set, options.
- One call site in `DndChatService` (shape `ChatOptions.Tools`).
- Config keys (threshold, exemplars) with code defaults (appsettings are git-crypt-masked → live overrides via env).
- No HTTP endpoint change (internal chat path) → no `.http`/`.insomnia` change.
- **Out of scope (Slice 2, deferred):** Bin-B set-return aggregation/join capability — this change only routes to the existing `search_entities`, it does not add set-returning query power.
