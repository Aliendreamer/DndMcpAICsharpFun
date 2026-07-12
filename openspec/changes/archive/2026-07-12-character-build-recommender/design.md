## Context

Slice A (`character-level-up-advice`) established the shared `Features/CharacterAdvice/` core: a grounding
contract (the assistant recommends only from returned cited `dnd_entities` menus, never inventing), an
`EntityOptionProvider` (cited `SubclassOptions(className, edition)` / `FeatOptions(edition)` /
`SpellOptions(className, spellLevel, edition)` over `IEntityRetrievalService`, edition-pinned to 2014), and
the `DndChatService` per-user in-process tool pattern (a tool added in the authenticated block, closing
over the signed-in `userId` when it touches owned data). `fivetools-field-fill` populated `dnd_entities`
with real structured fields (classes carry `hd`/`spellcastingAbility`/`proficiency`/`subclassTitle`;
subclasses carry `subclassFeatures`; spells carry `level`/`school`/`classes`), so a build recommendation
can ground on real mechanics. This slice turns "what should I make?" into a grounded build identity.

## Goals / Non-Goals

**Goals:** from a text concept (+ optional level), return a grounded build-option package — a validated
class + cited subclass/feat/spell menus + the class's structured build info — that the assistant turns
into a single-class build identity (class/subclass/key feats/signature spells/ability priorities), never
naming an entity that isn't a real cited record.

**Non-Goals:** a full level-by-level path (the level-up assistant handles leveling); multiclass build
paths (a multiclass concept gets the primary class + a noted dip → the level-up assistant); ownership /
owned-hero anchoring (that's slice C, build-critique); a UI surface (chat-tool-only this slice); any new
persistence/HTTP route/MCP-server tool.

## Decisions

**Two-stage grounding — class is judged+validated, everything below is menu-grounded.** A pure semantic
search over class entities by a *playstyle* concept ("tanky", "controller") ranks poorly, because class
rules text describes mechanics, not playstyle. So the **concept→class** step is the LLM's judgment (it
reliably knows "controller" → Wizard / Battle Master), and the service only **validates** the class exists
in `dnd_entities` (edition-pinned); when it doesn't (e.g. Artificer isn't in the 3-book corpus) the service
returns a `ClassInCorpus=false` result plus the **available class names** so the assistant re-picks.
Classes are a closed, validated set, so an invalid class is caught, never recommended. *Alternative:*
retrieve a class pool semantically — rejected (playstyle→class mismatch). *Alternative:* propose-then-
validate the whole build from model memory — rejected (fabrication-prone; rejected picks leave holes).

**Sub-picks are menu-grounded; feats/spells retrieved by the concept where semantic search works.**
Subclasses = *all* subclasses of the chosen class (a small set; `SubclassOptions`). Feats and spells =
**concept-relevant** cited retrieval — semantic search is reliable here because it runs over *effect* text
("control" → Web / Hold Person / Grease; control-oriented feats). Since the shipped `FeatOptions(edition)`
and `SpellOptions(className, spellLevel, edition)` don't take a free query, add an **optional concept
query parameter** to them (behavior-preserving when omitted, so the level-up caller is unaffected).
`targetLevel` bounds the reachable spell levels.

**Ability priorities are deterministic from structured fields.** The class entity's `spellcastingAbility`
and its save proficiencies (`proficiency`) give the primary-ability guidance; the LLM frames it. No new
computation.

**`BuildRecommendation` is a grounded option package, not the recommendation.** Exactly like
`plan_level_up` returns a delta+options and the LLM recommends, `RecommendBuildOptionsAsync` returns the
validated class + cited menus (or the not-found + available list); the assistant composes the actual
build and the "why it fits" narrative. This keeps the deterministic/LLM split identical to A.

**Not ownership-gated.** The recommender touches no owned data, so `recommend_build` takes no `userId`
(unlike `plan_level_up`). It's still registered in the authenticated tool block (unauthenticated → no
tool). This is the one intentional divergence from the other character tools, and the security-regression
test is extended to assert `recommend_build` exposes no `userId` argument.

## Risks / Trade-offs

- **[The LLM picks a bad concept→class match]** → this is the creative judgment we accept; the grounding
  only guarantees the *named entities are real*, not that the class is optimal. Acceptable — LLMs are
  strong at concept→class, and the assistant explains its reasoning for the user to judge.
- **[Fabricated feat/subclass/spell]** → structurally impossible: the assistant recommends only from the
  returned cited menus, and the tool description enforces it. The menus come from real retrieval.
- **[Concept retrieval returns nothing relevant]** → the tool still returns the class + subclass menu +
  a broader feat/spell set; the assistant recommends from what's there. An empty spell menu for a
  non-caster is correct (no spells).
- **[`FeatOptions`/`SpellOptions` signature change ripples to the level-up caller]** → the concept param
  is optional and defaulted, so `LevelUpAdviceService`'s existing calls are unchanged; the full suite
  proves behavior-neutrality.

## Migration Plan

No schema/data migration. Build order: (1) extend `FeatOptions`/`SpellOptions` with an optional concept
query (behavior-preserving); (2) `BuildRecommendation` record + `BuildRecommenderService` (validate class,
assemble menus) with unit tests; (3) the `recommend_build` chat tool + the security-regression extension.
Rollback is a code revert.

## Verification

Unit: `BuildRecommenderService` over a fake `IEntityRetrievalService` — valid class → structured build
info + cited subclass/feat/spell menus; class not in corpus → `ClassInCorpus=false` + available-class
names; the concept flows into feat/spell retrieval. The shipped `EntityOptionProvider` real-Qdrant test
already covers the cited retrieval; add a thin build-recommender integration case only if it earns it.
Security-regression: extend the `DndChatServiceTests` "no `userId` argument" test to cover
`recommend_build`. Build 0/0 + full `dotnet test` green (the `FeatOptions`/`SpellOptions` extension is
behavior-neutral for level-up). Chat-driven recommendation is smoke-only (needs Ollama), deferred like A.
