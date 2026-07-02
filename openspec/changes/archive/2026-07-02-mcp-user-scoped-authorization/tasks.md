## 1. Decide approach

- [x] 1.1 Choose (a) thread identity through the loopback MCP call, or (b) move character-scoped resolution to an in-process authenticated tool; record the decision in `design.md` — **Chose (b)**: identity from the shared-key loopback is not trustworthy (any key holder could assert an arbitrary user id), so the in-process tool closes over the authenticated session user instead. Implemented in c4b68d9.

## 2. Ownership enforcement

- [x] 2.1 Add an ownership-scoped snapshot lookup (snapshot → hero → campaign → user) to `HeroRepository` — `GetSnapshotForUserAsync`
- [x] 2.2 `CharacterResolutionService` verifies ownership against the calling user id; denies on mismatch — new `ResolveForUserAsync` throws `UnauthorizedAccessException` when not owned (shared `ResolveForSheetAsync`; `ResolveAsync` unchanged)
- [x] 2.3 Wire identity per the chosen approach — in-process `AIFunction` in `DndChatService.SendAsync` closes over the session user id (not a spoofable tool arg)
- [x] 2.4 Approach (b): drop `resolve_character_feature` from the shared-key MCP tool set — removed from `DndMcpTools` (`WithToolsFromAssembly` surface)

## 3. Tests + verify

- [x] 3.1 Test: owner resolves own snapshot succeeds (Red Dragonborn L11 breath weapon → fire, 15 ft. cone, DC 15)
- [x] 3.2 Test: cross-tenant snapshot id is denied — `ResolveForUserAsync(snapshot, otherUser)` throws `UnauthorizedAccessException`; `GetSnapshotForUserAsync(snapshot, otherUser)` returns null
- [x] 3.3 `dotnet build` + `dotnet test` green (847/847) — no `.http`/insomnia change (MCP tools are not HTTP endpoints; the tool moved in-process, no route changed)
