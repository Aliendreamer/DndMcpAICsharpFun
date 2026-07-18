## 1. Store — complete filter-set retrieval

- [ ] 1.1 Add `ListByFilterAsync(EntityFilters filters, int cap, CancellationToken ct)` to `IEntityVectorStore` returning `(int Total, IReadOnlyList<EntitySearchHit> Rows)` (or a dedicated lightweight hit).
- [ ] 1.2 Implement in `QdrantEntityVectorStore`: `client.CountAsync(collection, BuildFilter(filters))` → Total; `client.ScrollAsync(collection, filter, limit: cap, payloadSelector: true)` → rows (filter-only, no vector). Apply a hard max on `cap`.

## 2. Service + DTOs

- [ ] 2.1 Add `EntitySetRow` (`Id, Name, Type, SourceBook, Page, Cr?, SpellLevel?, DamageType?`) and `EntitySetResult { int Total, int Returned, IReadOnlyList<EntitySetRow> Rows }`. Compact — NO canonicalText / full fields.
- [ ] 2.2 Add `EntityRetrievalService.ListAsync(EntitySearchQuery/filters, int cap)` → `EntitySetResult`: call `ListByFilterAsync`, project payload → compact rows, set `Returned = min(Total, cap)`. Clamp cap to the configured default/max.

## 3. MCP tool

- [ ] 3.1 Add `list_entities` to `DndMcpTools` — params `type, crGte, crLte, spellLevel, damageType, keyword, sourceBook, srd, limit`; returns `{ total, returned, rows }`. Description contrasts it with `search_entities` ("COMPLETE set for all/every/how many" vs "semantic find-me-a"). `search_entities` untouched.

## 4. HTTP endpoint

- [ ] 4.1 Add `GET /retrieval/entities/list` in `EntityRetrievalEndpoints` (rate-limited `retrieval`, same query params as the tool) → `EntitySetResult`.
- [ ] 4.2 Update `DndMcpAICsharpFun.http` AND `dnd-mcp-api.insomnia.json` with the new endpoint (contract rule — same commit).

## 5. Router integration

- [ ] 5.1 Add `"list_entities"` to `ToolGroups.Map` in the `structured-lookup` group.

## 6. Tests

- [ ] 6.1 Service test: fake `IEntityVectorStore.ListByFilterAsync` returns a known set + total → assert `EntitySetResult` total/returned, compact rows (no canonicalText), and the truncation signal when `Total > cap`.
- [ ] 6.2 MCP-tool test: `list_entities` with filters → compact JSON `{total,returned,rows}`; assert `search_entities` behavior unaffected.
- [ ] 6.3 `ToolGroups` map test: `list_entities` resolves to `structured-lookup`.
- [ ] 6.4 Endpoint test (if a retrieval-endpoint test harness exists): `GET /retrieval/entities/list` returns the set shape.

## 7. Verify

- [ ] 7.1 `dotnet build` clean (warnings-as-errors) + full `dotnet test` green.
- [ ] 7.2 (Optional, manual) Live: `GET /retrieval/entities/list?type=Monster&crGte=5&crLte=5&limit=50` returns a complete CR-5 monster set with an honest total; a chat "list all level-3 fire spells" routes to `structured-lookup` and calls `list_entities`.
