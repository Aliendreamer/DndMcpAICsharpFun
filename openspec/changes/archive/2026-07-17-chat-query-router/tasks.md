## 1. Tool-group model + options

- [x] 1.1 `Features/Chat/Routing/ToolGroups.cs` — group consts (`retrieval-lore`, `structured-lookup`, `character-resolution`, `calculators`, `generation`) + static tool-name → group map (all real tool names mapped: search_lore/search_dnd/ask_setting_lore/ask_rules; search_entities/get_entity; resolve_character_feature/check_multiclass; calculate_crafting; generate_npc/party/prep_session/plan_downtime/plan_level_up/recommend_build/critique_build/rate_encounter/build_encounter). Unmapped → always-offered.
- [x] 1.2 `QueryRouterOptions` (binds `ChatQueryRouter`): `Enabled` (true), `Threshold` (0.45), `AlwaysSafeToolNames` (`["search_lore"]`), per-group `Exemplars`. Registered `.BindConfiguration`; code defaults authoritative (appsettings git-crypt-masked, env overrides live).

## 2. The hybrid classifier

- [x] 2.1 `QuerySignals` — deterministic high-precision regex pass (generation / character-resolution / structured-lookup). Fires ONLY when exactly one family matches (0 or ≥2 → null → embedding backstop), keeping the fast path precise.
- [x] 2.2 `ExemplarIndex` (+ `IExemplarIndex`) — per-group exemplar centroids via `IEmbeddingService`, computed once (singleton; resolves the scoped embedding service through `IServiceScopeFactory` to avoid a captive dependency), L2-normalized; `ClassifyAsync(queryVector)` → argmax-cosine `(group, confidence)`.
- [x] 2.3 `QueryRouter` — signal pass → else embed query + `IExemplarIndex` → threshold → narrowed list (`groupTools ∪ alwaysSafeCore ∪ unmapped`) or full list. Logs the decision `{path, group, confidence, offered/total}`.

## 3. Wire into chat

- [x] 3.1 `DndChatService` calls `queryRouter.RouteAsync(userMessage, toolList, ct)` immediately before `ChatOptions.Tools`; `Tools = [.. offeredTools]`. Turn/streaming/execution unchanged.
- [x] 3.2 DI (`ChatExtensions`): `QueryRouterOptions` bound; `IExemplarIndex → ExemplarIndex` singleton; `QueryRouter` scoped. Container scope-validation test green.

## 4. Tests

- [x] 4.1 Signal cases: per-family queries → asserted group; ambiguous/absent → null (`QueryRouterTests`).
- [x] 4.2 Embedding cases: fake `IEmbeddingService` + real `ExemplarIndex` (scoped fake via `ServiceCollection`) → asserted argmax group + confidence; below-threshold → full-set fallback.
- [x] 4.3 Safety: always-safe core in EVERY narrowed set; unmapped tool always offered; low-confidence/disabled → full set.
- [x] 4.4 Per-group representative query fixtures (signal theory).
- [x] 4.5 `DndChatService` integration: enabled router + "what is my breath weapon" → `client.LastOptions.Tools` narrowed to `resolve_character_feature` + `search_lore`, excludes `search_entities` (asserts the narrowed set reaches the LLM turn).

## 5. Verify

- [x] 5.1 `dotnet build` clean (0 warn/0 err) + full `dotnet test` green: **1464/1464** (+17 cases).
- [ ] 5.2 (Optional, manual) Live chat smoke: a character-resolution query, a structured-lookup query, and a narrative query each route to the expected group (check the routing-decision log); confirm no regression when confidence is low. Not run — the unit + `DndChatService` integration tests cover the contract; live smoke is optional confirmation.
