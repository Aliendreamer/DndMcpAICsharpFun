## Context

The companion already has a deterministic character-computation layer (`CharacterResolutionService`:
spell slots, save DC, spell attack, breath weapon, `ResolveMulticlassValidity`) exposed as per-user,
ownership-gated chat tools (`resolve_character_feature`, `check_multiclass`), and a parallel encounter
surface (`EncounterDesignService`) that pairs a deterministic math core with an LLM reasoning layer and
per-user `rate_encounter`/`build_encounter` tools. The character sheet (`CharacterSheet`) carries the
full multiclass `Classes` list, ability scores, proficiencies, features, and spellcasting. Typed
entities (`dnd_entities`) carry structured `ClassFields` (`Hd`, `classFeatures` as 5etools refs like
`"Action Surge|Fighter|PHB|2"`), `SubclassFields` (`subclassFeatures`, `ClassName`), `FeatFields`, and
`SpellFields`, searchable via `IEntityRetrievalService.SearchAsync(EntitySearchQuery{Type,Edition,SpellLevel,…})`.

Level-up advice is the missing *reasoning* layer on top of that computation. This is slice 1 of a
"character coach"; the shared core it establishes (a grounded delta + cited option menus + an
ownership-gated per-user orchestrator) is what the later concept-recommender and build-critique slices reuse.

## Goals / Non-Goals

**Goals:** for a hero the caller owns, compute a rule-grounded level-up delta for a chosen advancement
(existing class or a new-class dip) and a menu of the real, cited options each open choice unlocks;
recommend a specific pick with reasons (in chat) constrained to those cited options; show the
deterministic delta as a HeroDetail grounded card.

**Non-Goals:** deep optimization within class-specific sub-pools (invocations, metamagic, maneuvers,
expertise, fighting style) — surfaced, not optimized; from-scratch concept builds (slice B) and full
build critique (slice C); any new persistence/migration/HTTP route/MCP surface; equipment/magic-item advice.

## Decisions

**Deterministic core + LLM recommendation, mirroring the encounter surface.** `LevelUpPlanner` computes
a `LevelUpDelta` purely from rule data — HP from `ClassFields.Hd`, proficiency bonus from
`CharacterSheet.ProficiencyBonusForLevel`, the spell-slot diff by reusing the shipped slot logic in
`CharacterResolutionService`, and the features/choice-points by parsing `classFeatures`/`subclassFeatures`
for the target level. The LLM only *recommends over* the returned cited options. *Alternative:* let the
LLM derive "what you get at level N" from prose — rejected; that reintroduces the fabrication risk the
deterministic core exists to remove.

**Options come from real typed queries; the LLM never invents one.** `SubclassOptions`/`FeatOptions`/
`SpellOptions` query `dnd_entities` by `Type` (+ `Edition`, `SpellLevel`; subclasses post-filtered on
`SubclassFields.ClassName`) and return cited refs (`id + name + source`). The `plan_level_up` tool hands
the LLM this menu; the recommendation is constrained to it — the exact "encounter monsters come from real
retrieval" contract. *Alternative:* let the model name feats/spells from training — rejected (fabrication).

**One ownership-gated orchestrator returns a structured `LevelUpAdvice`.**
`LevelUpAdviceService.PlanForUserAsync(heroSnapshotId, userId, targetClass?, considerDip?)` resolves the
owned `HeroSnapshot` (or throws — the shipped `*ForUserAsync` gate) and assembles, per candidate
advancement (each existing class, plus new-class dips when `considerDip`), a `{delta + cited option
menus}`; for dip candidates it folds in the shipped `ResolveMulticlassValidity` so an illegal dip is
reported as such rather than recommended. `targetClass` omitted → advice for every existing class.

**Both surfaces read the same service; the split is facts vs opinion.** The HeroDetail card is a pure
display over `LevelUpAdviceService` (no LLM) — the rule-grounded delta is UI-worthy on its own — with a
"Ask the assistant to recommend →" action that prefills chat with `plan_level_up` for the hero. The
opinionated recommendation lives only in chat, where reasoning belongs. *Alternative:* render the
recommendation inline in the UI — rejected; it would need the LLM on a page render and blur the
facts/opinion boundary.

**Class-specific sub-pools are surfaced, not optimized.** When `classFeatures` parsing shows a level opens
a class-specific choice (e.g. a Warlock invocation), the delta *names that the choice exists* but slice 1
does not enumerate/rank that pool. Keeps the slice bounded while the delta stays honest about what the
level entails.

## Risks / Trade-offs

- **[`classFeatures`/`subclassFeatures` are semi-structured `JsonElement` 5etools refs]** → parse defensively
  (`"Name|Class|Source|Level"` and object forms); an unparseable ref is skipped with a logged note, never
  guessed. Unit-test the parser against real PHB class data for both editions.
- **[Spell-slot diff for the *next* level, incl. multiclass casters]** → reuse the shipped
  `CharacterResolutionService` slot logic evaluated at level+1 rather than reimplementing the caster-level
  table (single source of truth; no drift). Test a multiclass caster case.
- **[Edition drift]** → drive edition from the hero's class entity; where a rule (ASI levels, subclass
  timing) is edition-specific, derive it from the entity data (the level a feature/ASI first appears), not a
  hardcoded table. If the data can't express it, the delta states the limitation rather than inventing.
- **[Ownership]** → the orchestrator is `*ForUserAsync` only; a ship-blocking negative test proves caller A
  cannot plan caller B's hero. The tool closes over the session `userId` (never a tool argument).
- **[Fabrication]** → the tool returns only cited options; the tool description instructs the model to
  recommend strictly from them. (The model itself isn't unit-testable; the *menu* it's constrained to is.)

## Migration Plan

No schema/data migration. Build order: (1) `LevelUpDelta` + `LevelUpPlanner` (deterministic, unit-tested);
(2) option providers (real-Qdrant integration tests); (3) `LevelUpAdviceService` (ownership + dip tests);
(4) `plan_level_up` chat tool; (5) HeroDetail grounded card + Playwright. Rollback is a code revert.

## Verification

`LevelUpPlanner` unit tests (HP/PB/slot-diff/features-gained for known class+level cases, both editions
where the entity data supports it); option-provider integration tests vs **real Qdrant** (`QdrantFixture`
— a seeded class returns its real subclasses/feats/spells, cited); `LevelUpAdviceService` ownership
**negative** test + a dip-candidate test folding in `check_multiclass` validity; build 0/0 + full
`dotnet test` green; live Playwright of the HeroDetail card (rebuild the app image first — `docker compose
build app && docker compose up -d app`) desktop + mobile, no h-overflow. The chat-driven recommendation is
smoke-only (needs Ollama), noted deferred like the other chat surfaces.
