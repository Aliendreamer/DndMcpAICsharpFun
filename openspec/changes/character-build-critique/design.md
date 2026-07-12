## Context

Slices A and B established the shared `Features/CharacterAdvice/` core: an ownership-gated `*ForUserAsync`
pattern (resolve the owned `HeroSnapshot` via `HeroRepository.GetSnapshotForUserAsync(snapshotId, userId)`
or throw), a `ClassFeatureRefParser` that turns a class entity's 5etools `classFeatures` into
`(name, source, level)`, an edition-pinned (`Edition2014`) class-entity lookup, `EntityOptionProvider`,
and the grounding contract (reference only real cited entities). `CharacterResolutionService.ResolveForSheetAsync(sheet, feature, ct)`
computes derived facts (`"spell save dc"`, `"spell attack"`, `"spell slots"`). `fivetools-field-fill`
populated the class entities with structured `spellcastingAbility`/`proficiency`. Slice C reviews an
existing owned hero and reports what's off — the final character-coach surface.

## Goals / Non-Goals

**Goals:** for an owned hero, compute deterministic grounded findings (untaken choices, stat consistency,
ability alignment) — each anchored to a concrete sheet fact + real cited rule data — and return them as a
findings package the assistant frames into a critique; surface it as a chat tool + a HeroDetail card that
hand off to the level-up/build tools.

**Non-Goals:** free-form "this build is bad" judgment (every finding is fact-anchored); proficiency-hygiene
findings (duplicates/waste) and deep multiclass analysis (deferred D/E); the fuzzy "did you take an ASI or
a feat at this level" inference (deferred); any new persistence/HTTP route/MCP-server tool.

## Decisions

**Deterministic grounded findings, LLM frames — never free-judge.** C is the slice most tempting to
hand-wave ("your build is weak"). So the service emits a set of `CritiqueFinding`s, each tied to a concrete
fact about the actual sheet and, where relevant, a cited rule entity; the LLM's job is to *frame* those
into a critique, not to invent verdicts. Same deterministic-core + LLM-framing split as A/B. *Alternative:*
let the LLM read the sheet and critique freely — rejected; it's fabrication-/meta-gaming-prone and breaks
the grounding contract.

**Reuse A's per-level parsing for "untaken choices."** For each of the character's classes, fetch the
class entity (edition-pinned, mirroring `LevelUpAdviceService`'s lookup) and parse its `classFeatures`
with the shipped `ClassFeatureRefParser` for levels 1..currentClassLevel. Two grounded checks:
(i) **subclass not chosen** — `sheet.Classes[i].Subclass` is empty but the class's subclass-selection level
has passed (detected the same way A detects `IsSubclassSelectionLevel` — the earliest level a
subclass-title feature appears); (ii) **expected features not recorded** — the parsed features up to the
character's level, normalized-name-matched against `sheet.Features` names, minus what's present. Name
normalization mirrors `EntityNameIndex.Normalize` (case/punctuation-insensitive) to avoid false "missing"
on formatting differences.

**Stat consistency reuses the shipped resolver.** Compute `"spell save dc"`, `"spell attack"`, and
`"spell slots"` via `CharacterResolutionService.ResolveForSheetAsync(sheet, …)` and compare to the sheet's
recorded `SpellSaveDC`/`SpellAttackBonus`/`SpellSlots`; a mismatch is a finding. No new math.

**Ability alignment from the structured field.** Read the class's `spellcastingAbility` from the class
entity's `Fields`; if present and the character's highest ability score isn't that ability, that's a
finding. Non-casters skip this check (no spellcasting ability).

**`BuildCritique` is the findings package, not the critique.** Like `plan_level_up` returns a delta and the
LLM recommends, `CritiqueForUserAsync` returns `BuildCritique` (findings + optional strengths); the
assistant composes the prose critique and the hand-offs. Keeps the deterministic/LLM split identical to A.

**Ownership-gated, two surfaces mirroring A.** The service is `*ForUserAsync` only (resolve owned snapshot
or throw). `critique_build(heroSnapshotId)` closes over the session `userId` (no `userId` arg). The
HeroDetail "Review this build" card reuses A's grounded-card + `?prompt=` hand-off verbatim. A finding
hands off: no-subclass → `plan_level_up`; ability-misaligned → `recommend_build` (the LLM chains, since all
tools share the chat).

## Risks / Trade-offs

- **[False "missing feature" on name-formatting differences]** → normalize both sides
  (`EntityNameIndex.Normalize`) before the set difference; test a formatting variant ("Extra Attack" vs
  "Extra Attack (1)") to confirm it doesn't false-positive.
- **[Ownership leak]** → the service is `*ForUserAsync` only; a ship-blocking negative test proves caller A
  cannot critique caller B's hero. The tool closes over the session `userId`.
- **[Vacuous security guard for the new tool]** → per the dev-flow gate, `critique_build` is added to BOTH
  the no-`userId`-arg filter AND the auth-present/unauth-absent presence tests, so its registration is
  genuinely guarded.
- **[Multiclass sheets]** → run the untaken-choices + alignment checks per class; the alignment check uses
  each class's own spellcasting ability. Stat consistency uses the multiclass-aware resolver.

## Migration Plan

No schema/data migration. Build order: (1) `BuildCritique` record + `BuildCritiqueService` (the three
finding computations) with unit tests + the ownership negative test; (2) the `critique_build` chat tool +
the security/presence test coverage; (3) the HeroDetail "Review this build" card + Playwright. Rollback is
a code revert.

## Verification

Unit: `BuildCritiqueService` over a fake `IEntityRetrievalService` + seeded sheets — level-5 Fighter
missing "Extra Attack" → untaken-feature finding; level-3+ hero with empty subclass → subclass-not-chosen;
recorded save DC ≠ computed → stat-mismatch; caster whose highest ability ≠ `spellcastingAbility` →
alignment finding; a clean, fully-recorded build → no findings; a formatting-variant feature name → no
false "missing". Ownership **negative** test (caller A cannot critique caller B → throws) — ship blocker.
`critique_build` added to the no-`userId` filter + the present/absent presence tests. Build 0/0 + full
`dotnet test` green. HeroDetail card via live Playwright (rebuild the app image first) — findings render
for a real hero, desktop + mobile, no h-overflow. Chat-driven framing is smoke-only (needs Ollama),
deferred like A/B.
