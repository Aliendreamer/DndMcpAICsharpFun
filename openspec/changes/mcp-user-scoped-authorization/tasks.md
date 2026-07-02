## 1. Decide approach

- [ ] 1.1 Choose (a) thread identity through the loopback MCP call, or (b) move character-scoped resolution to an in-process authenticated tool; record the decision in `design.md`

## 2. Ownership enforcement

- [ ] 2.1 Add an ownership-scoped snapshot lookup (snapshot → hero → campaign → user) to `HeroRepository`
- [ ] 2.2 `CharacterResolutionService.ResolveAsync` verifies ownership against the calling user id; denies/empties on mismatch
- [ ] 2.3 Wire identity per the chosen approach (loopback param, or in-process tool closing over the session user)
- [ ] 2.4 If approach (b): drop `resolve_character_feature` from the shared-key MCP tool set

## 3. Tests + verify

- [ ] 3.1 Test: owner resolves own snapshot succeeds
- [ ] 3.2 Test: cross-tenant snapshot id is denied (no data leak)
- [ ] 3.3 `dotnet build` + `dotnet test` green; update `.http`/insomnia if the MCP tool surface changed
