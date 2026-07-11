## Why

The companion computes character facts (spell slots, save DC, multiclass validity) but never *advises*.
When a player levels up they face real choices — take an ASI or a feat, which subclass, which new
spells, is a multiclass dip worth it — and today the companion can't help ground those choices in the
character's actual sheet and the real rules. This is the first **reasoning** surface for the "character
build" north-star pillar, and the first slice of a "character coach" (concept-recommender and
build-critique follow on the same shared core).

## What Changes

- **A per-user level-up assistant** that, for a hero the signed-in user owns, computes the rule-grounded
  "what you gain at the next level" for a chosen advancement and recommends a specific pick with reasons —
  where every option it names (feat, subclass, spell) is a real cited `dnd_entities` record, never
  invented.
- **Covers advancing an existing class** (single-class or a chosen class of a multiclass hero) **and
  recommending a new-class dip** (reusing the shipped multiclass-validity check).
- **Two surfaces:** a `plan_level_up` chat tool (the assistant narrates + recommends), and a HeroDetail
  **"Plan level-up"** button that renders the deterministic delta as an inline grounded card and hands off
  to chat for the opinionated recommendation.
- **No new domain persistence, HTTP route, or MCP surface** — a per-user in-process chat tool (the shipped
  SEC-08 closure pattern) + server-side Blazor. So **no `.http` / `.insomnia` change.**

Slice 1 recommends within the *universal* choices — ASI-vs-feat, subclass (when due), newly-available
spells — plus the dip. Class-specific sub-pools (invocations, metamagic, maneuvers, expertise, fighting
style) are **surfaced but not deep-optimized** (a later refinement slice).

## Capabilities

### New Capabilities

- `character-level-up-advice`: an ownership-gated level-up assistant that computes a rule-grounded
  level-up delta + cited option menus for a hero the caller owns, exposed as a per-user chat tool and a
  HeroDetail grounded card, whose recommendations are constrained to real cited entities.

## Impact

- **New code**: `Features/CharacterAdvice/` — `LevelUpDelta` (record), `LevelUpPlanner` (deterministic
  delta), option providers over `dnd_entities`, `LevelUpAdviceService.PlanForUserAsync` (ownership-gated
  orchestrator returning `LevelUpAdvice`); a `plan_level_up` per-user tool in `DndChatService.SendAsync`;
  a "Plan level-up" grounded card on `CompanionUI/Pages/Campaigns/HeroDetail.razor`.
- **Reused unchanged**: `CharacterResolutionService` (spell-slot logic + `ResolveMulticlassValidity`),
  `IEntityRetrievalService`/`EntitySearchQuery` (typed option queries), `HeroSnapshot`/`CharacterSheet`
  (`ClassFields.Hd`/`classFeatures`, `SubclassFields.subclassFeatures`), the per-user-tool closure pattern,
  `QdrantFixture` (integration tests).
- **No** new domain/persistence/migration, DI-surface, HTTP route, or MCP tool → no `.http`/`.insomnia`.
- **Verification**: `LevelUpPlanner` unit tests (HP/PB/slot-diff/features, both editions where data
  supports); option-provider integration tests vs **real Qdrant** (`QdrantFixture`); an ownership
  **negative** test + a dip-candidate test; live Playwright of the HeroDetail card (rebuild the app image
  first); the chat-driven recommendation is smoke-only (needs Ollama), noted deferred.
