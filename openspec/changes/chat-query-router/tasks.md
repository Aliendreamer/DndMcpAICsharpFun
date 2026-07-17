## 1. Tool-group model + options

- [ ] 1.1 Add `Features/Chat/Routing/ToolGroup.cs` — the group enum/consts (`retrieval-lore`, `structured-lookup`, `character-resolution`, `calculators`, `generation`) and a static tool-name → group map. Unmapped tool names resolve to "always-offered".
- [ ] 1.2 Add `QueryRouterOptions` (bound from a `ChatQueryRouter` config section): `Threshold` (double, default ~0.45), `AlwaysSafeToolNames` (default = the fused prose-search tool name), per-group `Exemplars` (string phrases, code defaults). Register with `.BindConfiguration` + code defaults (appsettings git-crypt-masked; env overrides live).

## 2. The hybrid classifier

- [ ] 2.1 Add `QuerySignals` — deterministic high-precision regex/keyword pass: possessive/character-referential → `character-resolution`; set/quantifier → `structured-lookup`; imperative-create → `generation`. Returns `(group, confidence=1.0)` on a hit, else none.
- [ ] 2.2 Add `ExemplarIndex` — precompute (once, cached) per-group exemplar centroids via `IEmbeddingService`; `Classify(queryVector)` → `(group, maxCosine)`.
- [ ] 2.3 Add `QueryRouter` — orchestrates: signal pass → else embed query + `ExemplarIndex` → apply threshold → return the narrowed tool list (`groupTools ∪ alwaysSafeCore`) or the full list. Emit the routing-decision log/metric (`{group, confidence, path, offeredCount, totalCount}`).

## 3. Wire into chat

- [ ] 3.1 In `DndChatService`, call `QueryRouter.Route(latestUserMessage, toolList)` immediately before building `ChatOptions.Tools`; feed its result into `Tools`. No other change to the turn/streaming/execution.
- [ ] 3.2 Register `QueryRouter` + `QueryRouterOptions` + `ExemplarIndex` in DI (scoped/singleton as lifetime dictates; `ExemplarIndex` singleton so centroids compute once).

## 4. Tests

- [ ] 4.1 Signal cases: representative queries per signal → asserted group at confidence 1.0.
- [ ] 4.2 Embedding cases: a fake `IEmbeddingService` returning controlled vectors → asserted argmax group + confidence; a below-threshold vector → full-set fallback.
- [ ] 4.3 Safety: always-safe core present in EVERY narrowed set; unmapped tool always offered; empty/low-confidence → full set (== pre-router list).
- [ ] 4.4 Per-group representative query fixture (a handful each) → asserted routed group.
- [ ] 4.5 `DndChatService` test: the narrowed `Tools` list reaches the `ChatOptions` used for the LLM turn (mock the chat client, assert the tool names offered).

## 5. Verify

- [ ] 5.1 `dotnet build` clean (warnings-as-errors) + full `dotnet test` green.
- [ ] 5.2 (Optional, manual) Live chat smoke: a character-resolution query, a structured-lookup query, and a narrative query each route to the expected group (check the routing-decision log); confirm no regression when confidence is low (full set offered).
