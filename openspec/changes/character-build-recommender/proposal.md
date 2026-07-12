## Why

The companion can plan a *level-up* for a hero you already have (slice A), but it can't yet answer
"what should I *make*?" A player with a concept — "a tanky dwarf who controls the battlefield" — has no
grounded way to turn it into a real, rules-legal starting build. This is character-coach **slice B**: a
concept-to-build recommender that turns a fuzzy concept into a cited build identity, then hands the
*leveling* to the shipped level-up assistant.

## What Changes

- **A concept-to-build recommender** that, from a text concept (+ optional target level), recommends the
  core build **identity** — class, subclass, a few key feats, signature spells, and ability-score
  priorities — where **every named subclass/feat/spell is a real cited `dnd_entities` record** and the
  class is validated to exist. Nothing invented.
- **Two-stage grounding** (playstyle words like "controller" don't embed over class rules text): the LLM
  matches the concept to a **class** (its judgment), the tool **validates the class exists** (and returns
  the available classes when it doesn't), and everything below the class — **subclass/feats/spells** — is
  **menu-grounded** via the shipped `EntityOptionProvider`, with feats/spells retrieved by the *concept*
  (semantic search works over effect text).
- **Single-class** builds for slice 1; a multiclass concept gets the primary class + a noted dip direction,
  handed to the level-up assistant.
- **Chat-tool-only** — a new per-user in-process tool `recommend_build(className, concept, targetLevel?)`.
  Authenticated but **not ownership-gated** (no owned data). No new HTTP route or MCP-server tool → no
  `.http`/`.insomnia` change.

## Capabilities

### New Capabilities

- `character-build-recommender`: a concept-to-build recommender that returns a grounded build-option
  package (validated class + cited subclass/feat/spell menus) for a text concept, exposed as a per-user
  chat tool, constraining the assistant's recommendation to real cited entities.

## Impact

- **New code**: `Features/CharacterAdvice/BuildRecommendation.cs` (the grounded option package record) +
  `BuildRecommenderService.cs` (validate class + assemble cited menus, not ownership-gated); a
  `recommend_build` per-user tool in `DndChatService.SendAsync`.
- **Small extension**: `EntityOptionProvider.FeatOptions`/`SpellOptions` gain an optional concept query so
  feats/spells can be retrieved by the concept (today they're edition-/level-scoped only).
- **Reused**: `EntityOptionProvider.SubclassOptions`, `IEntityRetrievalService`/`EntitySearchQuery`
  (edition-pinned), the `DndChatService` per-user-tool closure pattern, the grounding contract, the
  `AddCharacterAdvice` DI group (already pulled in by `AddDndChat`).
- **No** new persistence/migration/HTTP route/MCP tool → no `.http`/`.insomnia` change.
- **Verification**: unit tests (valid class → structured info + cited menus; class not in corpus →
  available-class list; concept flows into feat/spell retrieval); the shipped real-Qdrant option-provider
  coverage; a security-regression extension (`recommend_build` exposes no `userId` arg); chat-driven
  recommendation is smoke-only (needs Ollama), deferred like slice A.
