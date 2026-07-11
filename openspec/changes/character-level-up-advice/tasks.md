## 1. Deterministic level-up delta core

- [ ] 1.1 Add `Features/CharacterAdvice/LevelUpDelta.cs` — record: `NewLevel`, `HpGain` (average + rolled option), `ProficiencyBonusBefore`/`After`, `SpellSlotsDelta`, `FeaturesGained` (each cited `{name, source}`), `OpenChoices` (AbilityScoreOrFeat, Subclass, Spells), `IsSubclassSelectionLevel`.
- [ ] 1.2 Add `LevelUpPlanner` — compute HP from `ClassFields.Hd`, PB from `CharacterSheet.ProficiencyBonusForLevel`, the spell-slot diff by reusing `CharacterResolutionService`'s slot logic at level+1, and features/choice-points by parsing `ClassFields.classFeatures` / `SubclassFields.subclassFeatures` for the target level (5etools ref `"Name|Class|Source|Level"` and object forms; unparseable ref skipped + logged).
- [ ] 1.3 Unit-test `LevelUpPlanner` against known class+level cases (HP/PB/slot-diff/features-gained), both editions where the entity data supports it, incl. a multiclass caster slot case.
- [ ] 1.4 Build 0/0; suite green.

## 2. Cited option providers over dnd_entities

- [ ] 2.1 Add `SubclassOptions(class, edition)` / `FeatOptions(edition)` / `SpellOptions(class, spellLevel, edition)` over `IEntityRetrievalService.SearchAsync` (`EntitySearchQuery` `Type`/`Edition`/`SpellLevel`; subclasses post-filtered on `SubclassFields.ClassName`). Each returns cited refs (`id + name + source`).
- [ ] 2.2 Integration-test each provider vs **real Qdrant** (`QdrantFixture`, GUID-suffixed collection): a seeded class returns its real subclasses/feats/spells, cited; a non-vacuity check (a deliberate wrong filter returns nothing).
- [ ] 2.3 Build 0/0; suite green (Docker up).

## 3. Ownership-gated orchestrator

- [ ] 3.1 Add `LevelUpAdviceService.PlanForUserAsync(heroSnapshotId, userId, targetClass?, considerDip?)` — resolve the owned `HeroSnapshot` (or throw), assemble per-candidate `{LevelUpDelta + cited option menus}` for each existing class (+ new-class dips when `considerDip`), folding in `CharacterResolutionService.ResolveMulticlassValidity` for dip candidates. Return a structured `LevelUpAdvice`.
- [ ] 3.2 Ownership **negative** test (caller A cannot plan caller B's hero → throws) + a dip-candidate test (legal dip = candidate; illegal dip = marked not-allowed with the failed prerequisite).
- [ ] 3.3 Register the service(s) in the correct `Add*` group (and its test replica if a scope-validation gate covers it). Build 0/0; **full** suite green.

## 4. `plan_level_up` chat tool

- [ ] 4.1 In `DndChatService.SendAsync`, add the per-user in-process tool `plan_level_up(heroSnapshotId, targetClass?, considerDip?)` closing over the signed-in `userId` (alongside the shipped character/encounter tools; unauthenticated → not added). Description instructs the model to recommend strictly from the returned cited options.
- [ ] 4.2 Build 0/0; suite green. (Chat-driven recommendation is smoke-only — needs Ollama — noted deferred.)

## 5. HeroDetail grounded card

- [ ] 5.1 Add a "Plan level-up" action to `CompanionUI/Pages/Campaigns/HeroDetail.razor` that renders the deterministic `LevelUpDelta` as an inline grounded card (display-only, no LLM) via `LevelUpAdviceService`, plus an "Ask the assistant to recommend →" action that continues in chat with `plan_level_up` for the hero.
- [ ] 5.2 Build 0/0; full suite green (behavior-neutral for the rest of HeroDetail).
- [ ] 5.3 Live Playwright (rebuild the app image first — `docker compose build app && docker compose up -d app`): the grounded card renders for a real hero, desktop + mobile, no horizontal overflow; the recommend action routes to chat.
