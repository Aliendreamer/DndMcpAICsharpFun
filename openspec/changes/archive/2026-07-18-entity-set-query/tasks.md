## 1. Store — complete filter-set retrieval

- [x] 1.1 Added `ListByFilterAsync(EntityFilters, int cap, CancellationToken)` → `(int Total, IReadOnlyList<EntitySearchHit> Rows)` to `IEntityVectorStore`.
- [x] 1.2 Implemented in `QdrantEntityVectorStore`: `client.CountAsync(BuildFilter(filters))` → Total; `client.ScrollAsync(filter, limit: cap, payloadSelector)` → rows (filter-only, no vector). `[ExcludeFromCodeCoverage]` store — verified via the service-layer fake.

## 2. Service + DTOs

- [x] 2.1 `EntitySetRow` (`Id, Type, Name, SourceBook, Page, Cr?, SpellLevel?, DamageType?`) + `EntitySetResult { Total, Returned, Rows }` — compact, no canonicalText.
- [x] 2.2 `EntityRetrievalService.ListAsync(query, cap)` → `EntitySetResult`: clamp cap (default 50, max 200), `ListByFilterAsync`, project payload → compact rows (discriminators best-effort from `Fields`), `Returned = rows.Count`. Refactored the shared filter build into `BuildFilters(q)` (reused by `ExecuteAsync`).

## 3. MCP tool

- [x] 3.1 `list_entities` in `DndMcpTools` (`type, crMin, crMax, spellLevel, damageType, keyword, sourceBook, srd, limit`) → `{ total, returned, truncated, note, rows }`. Description contrasts it with `search_entities` (COMPLETE set for all/every/how many vs semantic find-me-a). `search_entities` untouched.

## 4. HTTP endpoint

- [x] 4.1 `GET /retrieval/entities/list` in `EntityRetrievalEndpoints` (rate-limited `retrieval`, filter-only via `ListPublic` → `svc.ListAsync`).
- [x] 4.2 Updated `DndMcpAICsharpFun.http` (two `/list` examples) AND `dnd-mcp-api.insomnia.json` (new request; JSON re-validated) — same commit.

## 5. Router integration

- [x] 5.1 Added `"list_entities"` to `ToolGroups.Map` in `structured-lookup`.

## 6. Tests

- [x] 6.1 Service test (`EntityRetrievalServiceRerankerTests.ListAsync_...`): fake store returns (137 total, 2 hits) → asserts Total=137, Returned=2, truncation signal, compact rows.
- [x] 6.2 MCP-tool test (`DndMcpToolsTests.list_entities_...`): compact JSON `{total,returned,truncated,rows}`; `search_entities` unaffected.
- [x] 6.3 `ToolGroups` map test: `list_entities` → `structured-lookup`.
- [~] 6.4 Endpoint test — no WebApplicationFactory retrieval-endpoint harness exists; the endpoint is a thin pass-through over the service-tested `ListAsync` (covered by 6.1/6.2). Skipped by design.

## 7. Verify

- [x] 7.1 `dotnet build` clean (0 warn/0 err) + full `dotnet test` green: **1467/1467** (+3 net).
- [ ] 7.2 (Optional, manual) Live: `GET /retrieval/entities/list?type=Monster&crNumeric_gte=5&crNumeric_lte=5&limit=50` returns a complete CR-5 monster set with honest total; chat "list all level-3 fire spells" routes to `structured-lookup` and calls `list_entities`. Not run — the unit/service/tool tests cover the contract.
