## 1. BuildCritique + BuildCritiqueService (findings)

- [ ] 1.1 Add `Features/CharacterAdvice/BuildCritique.cs` — records: `CritiqueFinding` (a `Kind` enum `UntakenChoice`/`StatConsistency`/`AbilityAlignment`, a grounded `Observation` string, an optional cited `{Id,Name,Source}`), and `BuildCritique(IReadOnlyList<CritiqueFinding> Findings, IReadOnlyList<string> Strengths)`.
- [ ] 1.2 Add `BuildCritiqueService.CritiqueForUserAsync(long heroSnapshotId, long userId, CancellationToken ct)`: resolve the owned snapshot via `HeroRepository.GetSnapshotForUserAsync` or throw `UnauthorizedAccessException` (verbatim message, like `LevelUpAdviceService`). Compute the three finding sets:
  - **(A) Untaken choices** — per class: fetch the class entity (edition-pinned lookup, mirroring `LevelUpAdviceService`), parse `classFeatures` via `ClassFeatureRefParser` for levels 1..classLevel; flag (i) subclass-not-chosen when the subclass-selection level has passed and `Subclass` is empty; (ii) parsed features up to level, `EntityNameIndex.Normalize`-matched against `sheet.Features` names, minus present → missing-feature findings (cited).
  - **(B) Stat consistency** — `CharacterResolutionService.ResolveForSheetAsync(sheet, "spell save dc"/"spell attack"/"spell slots", ct)` vs the sheet's recorded values; mismatch → finding.
  - **(C) Ability alignment** — read the class entity's `spellcastingAbility` from raw `Fields`; if present and the character's highest ability score isn't that ability → finding; non-casters skipped.
- [ ] 1.3 Register `BuildCritiqueService` in the `AddCharacterAdvice` DI group.
- [ ] 1.4 Unit-test with a fake `IEntityRetrievalService` + seeded sheets: level-5 Fighter missing "Extra Attack" → untaken-feature finding; empty subclass past its level → subclass finding; recorded save DC ≠ computed → stat finding; caster highest-ability ≠ spellcastingAbility → alignment finding; a clean build → no findings; a formatting-variant feature name → NOT flagged missing. **Ownership NEGATIVE test (caller A cannot critique caller B → throws) — SHIP BLOCKER.**
- [ ] 1.5 Build 0/0; full `dotnet test` green.

## 2. `critique_build` chat tool + security/presence tests

- [ ] 2.1 In `DndChatService.SendAsync`, add the per-user tool `critique_build(heroSnapshotId)` inside the authenticated `if (long.TryParse(idClaim, out var userId))` block, closing over `userId` (NO `userId` arg), calling `BuildCritiqueService.CritiqueForUserAsync`. Add the ctor dep. Description: frame the critique from the returned findings, hand off to `plan_level_up` (untaken choices) / `recommend_build` (misalignment) where relevant, never invent.
- [ ] 2.2 Extend the `DndChatServiceTests` no-`userId`-arg filter AND the auth-present/unauth-absent presence tests to include `critique_build` (dev-flow gate — both guards). Thread a `BuildCritiqueService` through the test ctor helper.
- [ ] 2.3 Build 0/0; full `dotnet test` green. (No `.http`/`.insomnia` change — in-process tool.)

## 3. HeroDetail "Review this build" card

- [ ] 3.1 Add a "Review this build" action to `CompanionUI/Pages/Campaigns/HeroDetail.razor` (parallel to "Plan level-up") that calls `BuildCritiqueService.CritiqueForUserAsync(_hero.LatestSnapshot.Id, _userId, default)` and renders the findings display-only (grouped by kind, each with its observation + cited rule), plus an "Ask the assistant to critique →" `?prompt=` hand-off (reuse the level-up card's pattern). Behavior-neutral for the rest of HeroDetail.
- [ ] 3.2 Build 0/0; full suite green.
- [ ] 3.3 Live Playwright (rebuild the app image first — `docker compose build app && docker compose up -d app`): the findings card renders for a real hero (seed a hero with a gap, e.g. a level-5 Fighter missing Extra Attack), desktop + mobile, no horizontal overflow; the critique action routes to chat.
