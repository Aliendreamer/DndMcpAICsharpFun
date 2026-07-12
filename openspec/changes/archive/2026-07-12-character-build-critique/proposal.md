## Why

The companion can plan a hero's next level (slice A) and recommend a build from a concept (slice B), but
it can't yet look at a character you already have and say what's *off* — a missing subclass, an
un-recorded class feature, a spell save DC that doesn't match the sheet, a casting stat that isn't your
highest score. This is character-coach **slice C** (the last): a build-critique anchored to an owned hero
that surfaces grounded findings the assistant frames into a critique — and hands off to the level-up and
build tools where a finding suggests it.

## What Changes

- **A build-critique** for a hero the signed-in user owns: a set of **deterministic, grounded findings** —
  each anchored to a concrete fact about the *actual* sheet + real cited rule data — that the assistant
  turns into a critique. Every "suboptimal" is tied to a fact; the assistant never free-judges.
- **Three finding sets** (slice 1): **(A) untaken choices** — a subclass not chosen when due, and class
  features the character should have by their level but the sheet doesn't record (parsed from the class's
  `classFeatures`); **(B) stat consistency** — the sheet's recorded spell save DC / spell attack / spell
  slots vs the computed values; **(C) ability alignment** — the character's highest ability vs the class's
  key ability.
- **Ownership-gated**, like slice A (resolve the owned snapshot or throw).
- **Two surfaces** (mirroring A): a `critique_build(heroSnapshotId)` per-user chat tool and a HeroDetail
  **"Review this build"** card that shows the findings and hands off to chat. No new HTTP route or
  MCP-server tool → no `.http`/`.insomnia` change.

## Capabilities

### New Capabilities

- `character-build-critique`: an ownership-gated build-critique that computes deterministic grounded
  findings (untaken choices, stat consistency, ability alignment) for an owned hero and returns them as a
  findings package the assistant frames, exposed as a per-user chat tool and a HeroDetail card.

## Impact

- **New code**: `Features/CharacterAdvice/BuildCritique.cs` (findings package record) +
  `BuildCritiqueService.cs` (ownership-gated, computes A/B/C findings); a `critique_build` per-user tool in
  `DndChatService.SendAsync`; a "Review this build" card on `HeroDetail.razor`.
- **Reused**: `ClassFeatureRefParser` + the edition-pinned class-entity lookup (slice A),
  `CharacterResolutionService.ResolveForSheetAsync` (computed stats), the structured class fields
  (`spellcastingAbility`/`proficiency`), `HeroRepository.GetSnapshotForUserAsync`, the `DndChatService`
  per-user-tool closure pattern, A's HeroDetail card + `?prompt=` hand-off, the `AddCharacterAdvice` DI
  group.
- **No** new persistence/migration/HTTP route/MCP tool → no `.http`/`.insomnia` change.
- **Verification**: unit tests (each finding fires on a seeded sheet; a clean build → no findings; an
  ownership **negative** test = ship blocker); the new tool joins BOTH the no-`userId` security filter and
  the auth-present/unauth-absent presence tests (dev-flow gate); the HeroDetail card via live Playwright
  (rebuild the app image first); chat-driven framing is smoke-only (needs Ollama), deferred like A/B.
