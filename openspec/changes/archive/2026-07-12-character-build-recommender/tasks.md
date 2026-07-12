## 1. Concept query on the option provider

- [ ] 1.1 Add an optional concept-query parameter to `EntityOptionProvider.FeatOptions` and `SpellOptions` (e.g. `string? concept = null`) that, when supplied, drives the `EntitySearchQuery.QueryText` for relevance; when omitted, behavior is unchanged (the level-up caller passes nothing).
- [ ] 1.2 Verify `LevelUpAdviceService`'s existing calls compile unchanged (optional param defaulted). Build 0/0; full suite green (behavior-neutral).

## 2. BuildRecommendation + BuildRecommenderService

- [ ] 2.1 Add `Features/CharacterAdvice/BuildRecommendation.cs` — a record: `ClassInCorpus` (bool), `ClassName`, structured build info (`HitDie`, `SpellcastingAbility`, `SaveProficiencies`, `SubclassTitle`), `Subclasses`/`Feats`/`Spells` (`IReadOnlyList<CitedOption>`), and `AvailableClasses` (`IReadOnlyList<string>`, populated when `ClassInCorpus` is false).
- [ ] 2.2 Add `BuildRecommenderService.RecommendBuildOptionsAsync(string className, string concept, int? targetLevel, DndVersion edition, CancellationToken ct)`: validate the class exists in `dnd_entities` (edition-pinned, mirroring the level-up class lookup); if not found, return `ClassInCorpus=false` + the available class names (a class-type search); else read the class entity's structured fields (`hd`/`spellcastingAbility`/`proficiency`/`subclassTitle`) and assemble cited menus via `EntityOptionProvider` — `SubclassOptions(className)`, `FeatOptions(edition, concept)`, `SpellOptions(className, <level from targetLevel>, edition, concept)`. Not ownership-gated.
- [ ] 2.3 Unit-test with a fake `IEntityRetrievalService`: valid class → structured info + cited menus (subclass/feat/spell), and the concept reaches the feat/spell retrieval; class not in corpus → `ClassInCorpus=false` + a non-empty `AvailableClasses`.
- [ ] 2.4 Register `BuildRecommenderService` in the `AddCharacterAdvice` DI group (already pulled in by `AddDndChat`). Build 0/0; full suite green.

## 3. `recommend_build` chat tool + security regression

- [ ] 3.1 In `DndChatService.SendAsync`, add the per-user tool `recommend_build(className, concept, targetLevel?)` in the authenticated block alongside the shipped character tools. It takes NO `userId` (not ownership-gated) and calls `BuildRecommenderService.RecommendBuildOptionsAsync`. The description instructs the assistant to pick the class for the concept, re-pick from `AvailableClasses` if not-in-corpus, and recommend subclass/feats/spells strictly from the returned cited options + ability priorities, explaining why it fits.
- [ ] 3.2 Extend the existing `DndChatServiceTests` "per-user tools don't expose a `userId` argument" test to include `recommend_build` (its schema must have no `userId` property).
- [ ] 3.3 Build 0/0; full `dotnet test` green. (Chat-driven recommendation is smoke-only — needs Ollama — deferred like slice A. No `.http`/`.insomnia` change — in-process tool, no HTTP/MCP surface.)
