# D&D Companion — Roadmap & Progress (living; refreshed 2026-07-12)

**North star:** a companion agent that REASONS (character build / encounter design / setting-aware
lore), not just retrieves. RAG + extraction are the *means*, and that foundation is built —
**all named reasoning-layer items (2, 3, 4) are now SHIPPED.** The frontier is now UN-named:
either resume the parked `prose-grounded-knowledge-model` re-architecture, or add net-new
companion reasoning surfaces (encounter design, setting-aware lore). Next item is a fresh brainstorm.

## Status legend: ✅ done · 🔄 in progress · ⬜ not started

## FOUNDATION — extraction + retrieval  ✅ (collapsed; see git history + archived changes)
Everything below shipped and is archived — do NOT re-plan it, just build on it:
- **WRITE-layer extraction quality:** recall fix + precision authoritative-allowlist + deterministic
  type resolution; generic 5etools backfill (`fivetools-entity-backfill`, archived 2026-07-04);
  **Object entity type + decline-not-leak** (archived 2026-07-05 — `mem:extraction/dmg_generic_backfill_status`).
- **Extraction perf — qwen3 /no_think:** SHIPPED (`803da7b`), ~8× faster, no classification regression.
- **Slice 1 character-fact-resolution:** shipped (`235699a`) — `CharacterResolutionService`,
  structured-fact store, `resolve_character_feature` MCP tool.

## FRONTIER — the REASONING layer (all named items DONE)
- **Item 2 — multiclass character** ✅ DONE (archived `multiclass-character`, 2026-07-05) + **HeroDetail
  multiclass-editing UI** ✅ (archived `hero-multiclass-editing`) + Playwright UI smoke passed.
- **Item 3 — Auto-NeedsReview grounding cascade** ✅ DONE (archived `entity-grounding-cascade`,
  2026-07-09; 11 code commits `e71d256..6721bc7` + 3 integration-test commits `0809074,8d4ef8e,e25d613`,
  base 42d56a9; build 0/0, FULL suite **1062/1062**).
  Built the missing Tier 1 + Tier 2 on top of existing Tier 0 as ONE shared `GroundingCascade`
  (`Features/Ingestion/EntityExtraction/`). `GroundingVerdict{Status:Grounded|Ungrounded|Uncertain,
  DecidedByTier,Score}` = pure `GroundingCombiner` of (Tier0 bool, Tier1 vs floor, Tier2 bool?).
  **Tier 0** = existing `Tier0FieldGrounding` OCR field-match (extracted `HasAnyFieldGrounded` helper,
  runner delegates). **Tier 1** (`Tier1EmbeddingGrounding` behind `ITier1Grounding`) = embed entity +
  `dnd_blocks` search scoped to the entity's OWN SourceBook + page window (`GroundingOptions`
  SimilarityFloor .5 / PageWindow 2); ESCALATION-GATE-ONLY — topical similarity ALONE never promotes
  (the Dragonborn-fabrication guard). **Tier 2** (`IGroundingJudge`/`QwenGroundingJudge`, shared
  `IChatClient` qwen3) = field-support judge ("not from general D&D knowledge"), **tri-state `bool?`**
  so I/O failure/unparseable → null → Uncertain (never a false fabrication). Verdict→action
  (`GroundingActionPolicy`, name-gated via `HasOcrArtifacts`): Grounded→promote (clear NeedsReview→
  Accepted) unless garbled name; Ungrounded→new `EntityDisposition.Ungrounded`; Uncertain→stay flagged.
  **`EntityDisposition.Ungrounded` is EXCLUDED from `dnd_entities` at ALL 3 write paths** (ingest skips,
  reground deletes, `ReindexEntityAsync` deletes-not-upserts — final-review caught the admin-accept
  leak). Extraction wires the cascade (judge OFF default = behavior-identical to before). Backlog:
  `POST /admin/books/{id}/reground-entities?judge=` (`RegroundService`) — Tier0/1 fast pass, `?judge=true`
  opts into Tier2; checkpointed/resumable `<slug>.reground.progress.json` (persists changedIds so a
  crash-resume still reindexes/deletes); writes canonical in place + targeted reindex/delete per changed
  entity; NEVER deletes canonical. Opt: cascade skips Tier1 when judge off (verdict-neutral).
  **REAL-QDRANT INTEGRATION TESTS ADDED (2026-07-09b):** `Tier1EmbeddingGroundingIntegrationTests`
  (4 facts — proves book+page scoping genuinely filters via a real Qdrant seed, non-vacuity verified by
  a deliberate filter break) + `RegroundServiceIntegrationTests` (real `QdrantEntityVectorStore` +
  `ReindexEntityAsync`, fake cascade/tracker — proves promote→indexed, Ungrounded→genuinely DELETED
  from real `dnd_entities`, i.e. the I-1 invariant end-to-end). Both GUID-suffix collections + clean up.
  STILL DEFERRED (non-blocking): fully-live manual smoke needing real Ollama (judge) + app; Minors —
  crash-window stale needs_review payload flag on promote; recount double-counts pre-existing Ungrounded;
  RegroundService bypasses NeedsReviewService per-path write lock (race, low); admin re-accept of an
  Ungrounded entity needs a disposition-clearing action (UX, out of scope).
- **Item 4 — Corpus-wide dedup** ✅ DONE (archived `corpus-wide-entity-dedup`, 2026-07-08;
  9 commits `bf0909c..d68e99b`; build 0/0, 1017/1017). Dedup key `(EntityNameIndex.Normalize(name),
  Type, Edition)` — editions never merge. Authority-first `DuplicateResolver`. Slice 1 = query-time
  collapse in `FusedRetrievalService` (winner carries group MAX score; prose untouched) = DURABLE
  correctness. Slice 2 = `GET /admin/retrieval/entities/duplicates` + `POST .../compact?apply=` (deletes
  ONLY loser Qdrant points; canonical never rewritten). `ScrollAllAsync`/`DeleteByIdsAsync`. First real
  Qdrant Testcontainer (`Testcontainers.Qdrant` 4.12.0 + `QdrantFixture`). `Features/Retrieval/Entities/
  Dedup/*`. DEFERRED: live-host smoke of the two dedup admin endpoints.

## COMPANION REASONING — net-new surfaces (the north star, now BUILDING)
- **Character level-up advice (character-coach slice 1)** ✅ DONE (archived `2026-07-11-character-level-up-advice`;
  commits `5012e9b`(spec)..`a80be05`; build 0/0, full suite **1227/1227**; final opus review CHANGES-REQUESTED→both
  findings fixed). SECOND companion-reasoning surface + slice 1 of a **"character coach"** (slices B concept-recommender
  + C build-critique planned on the SAME shared core). `Features/CharacterAdvice/`: deterministic `LevelUpPlanner`
  (HP from `ClassFields.Hd`, PB formula, spell-slot diff reusing `MulticlassSlotTableSeeder` PHB arrays +
  `MulticlassSpellcasting.ResolveSlotSource`, features/choices by parsing 5etools `classFeatures`) + `EntityOptionProvider`
  (cited subclass/feat/spell menus over `dnd_entities`) + ownership-gated `LevelUpAdviceService.PlanForUserAsync`
  (resolve owned snapshot or throw; per-candidate delta+options; advance-EXISTING class AND eligible NEW-class dips via
  `check_multiclass`/`ResolveMulticlassValidity`). Surfaces: `plan_level_up` per-user chat tool (closes over session
  userId; recommend-from-cited-menu contract) + a HeroDetail display-only GROUNDED CARD + a `?prompt=` chat hand-off.
  GROUNDING CONTRACT enforced: class/subclass resolved by EXACT name-match-or-SKIP (live Playwright smoke caught a
  wrong-class fallback — a Barbarian dip mislabeled with Warlock's hit die/features — fixed `7004db0`); Hd-null → SKIP
  not fabricate-d8 (final review, fixed `e9ad7c2`); 6-part subclassFeature ref parsing fixed too. **KNOWN LIMITATION
  (accepted — user chose ship-as-is):** the delta grounds on the STRUCTURED entity layer (`ClassFields.hd/classFeatures/
  subclassTitle`) which is THIN corpus-wide — running Qdrant has only 4 richly-structured classes (Bard/Ranger/Sorcerer/
  Warlock; live-verified working), the canonical corpus (`books/canonical`, 413 Class entities) is ~prose-only (0 hd,
  1 classFeatures, 0 Subclass entities). Feature degrades HONESTLY (grounds where structured data exists, skips where
  absent — never fabricates). **FOLLOW-UP (tracked):** grow coverage — either ENRICH structured class entities OR
  RETHINK level-up grounding toward PROSE (canonicalText + LLM), the north-star direction (`mem:project_entity_extraction_rethink`).
  Deferred Minors: HP floor-of-1 clamp; DipValidity discards the prereq reason (only the exclusion path is used).
  LESSON → dev-flow: a self-seeded integration test proves your CODE, not that the real corpus HAS the fields you consume.
- **`fivetools-field-fill` (the hybrid — extraction-favored)** ✅ DONE (archived `2026-07-12-fivetools-field-fill`;
  code commits `161b388`(+rename `a37a130`)/`e820058`/`41897c6`/`856a4b9`, base 94cc643; build 0/0, FULL suite
  **1239/1239**; final opus review READY TO MERGE, no findings). Restores the intended hybrid that the level-up
  grounding gap exposed: **extraction is the source of truth for ALL entities (99% on core books); 5etools ONLY
  patches missing STRUCTURED fields**, merged, never overwriting extraction/`entries`. `Features/Ingestion/
  FivetoolsIngestion/`: `FivetoolsFieldMerger` (pure fill-missing-only merge; per-field rule absent→fill /
  present&provenance-listed→re-derive / present&unlisted→untouch; provenance in reserved `_fivetoolsFilledFields`
  inside `Fields` — no schema change; idempotent byte-identical re-run) + `FieldFillAllowlist` (per-type structured
  allowlist, NEVER `entries`) + `EntityFieldFillService.FillAsync` (per-book: index the 5etools roster by
  Normalize(name)+source via `FivetoolsSourceRegistry`+`FivetoolsMapperRegistry`, merge each canonical entity, atomic
  write; skips `manual`; no-source-key no-op). `POST /admin/books/{id}/fill-fields` + auto-run wired into
  `EntityExtractionOrchestrator.ExtractAsync` (deterministic since 5etools static → can't decay). OPERATIONAL cleanup
  live-run 2026-07-12: filled PHB(12 classes+324 spells)/MM(446 monsters)/DMG(3), re-ingested, deleted 8593
  `data_source:5etools` strays → `dnd_entities` = 2307 extraction entities only; level-up grounds all 12 classes from
  extraction+fill (live-verified). Filled canonicals committed `6d57961`. DEFERRED (final-review Minors): doc-only spec
  touch-up (`_fivetoolsFilledFields` vs design's `fivetoolsFilledFields`; allowlist uses real 5etools field names);
  Normalize last-wins (pre-existing pattern); no cross-call write lock (deterministic→safe); FUTURE-LANDMINE — if the
  allowlist ever extends to shared-5etools-file types (Item/MagicItem, Race/Subrace) add a rarity/array-key split.
  Optional follow-up: `backfill-spells` (`data_source:5etools-backfill`) for any official spell GAPS extraction missed.
- **Character build recommender (character-coach slice B)** ✅ DONE (archived `2026-07-12-character-build-recommender`;
  7 commits `d918009`..`c44d6cb`, base d97784f; build 0/0, FULL suite **1244/1244**; final opus review READY TO MERGE).
  Concept→build IDENTITY (class+subclass+key feats+signature spells+ability priorities) from a pure TEXT CONCEPT
  (not ownership-gated) + optional targetLevel, on the shipped `Features/CharacterAdvice/` core. TWO-STAGE GROUNDING:
  the LLM picks the class (concept→class judgment), `BuildRecommenderService` VALIDATES it exists (edition-pinned
  Edition2014; not-found → `ClassInCorpus=false` + available class names → LLM re-picks), then the sub-picks
  (subclass/feat/spell) are MENU-GROUNDED via `EntityOptionProvider` — feats/spells retrieved by the CONCEPT
  (extended `FeatOptions`/`SpellOptions` with an optional trailing concept query, behavior-neutral for slice A);
  spells bounded by targetLevel (range 1..clamp((L+1)/2,1,9)). Ability priorities deterministic from the class's
  structured fields. `BuildRecommendation` = the grounded option PACKAGE (LLM composes the build; never invents).
  `recommend_build(className, concept, targetLevel?)` per-user chat tool — NOT ownership-gated (no userId), in the
  auth block, security-regression + presence tests guard it (dev-flow: new per-user tool needs BOTH guard tests).
  SINGLE-CLASS (multiclass concept → primary class + note dip → level-up assistant). CHAT-TOOL-ONLY (no UI → no
  Playwright gate; chat smoke deferred, needs Ollama). DEFERRED: UI entry (Scratchpad "build ideas" box); multiclass
  build paths; half-caster spell-level exactness (full-caster approx).
- **Character build critique (character-coach slice C — the LAST)** ✅ DONE (archived `2026-07-12-character-build-critique`;
  5 code commits `a5b5412`..`ac8eda1` (spec base `4999f18`) + plan/gitignore `2025cc4` + archive; build 0/0, FULL suite
  **1253/1253**; final opus review CHANGES-REQUESTED→fixed in-loop→READY). Reviews an OWNED hero and emits DETERMINISTIC
  GROUNDED FINDINGS the LLM frames (never free-judges). `Features/CharacterAdvice/`: `BuildCritique`(HeroSnapshotId,
  Findings, Strengths) + `CritiqueFinding`(Kind{UntakenChoice|StatConsistency|AbilityAlignment}, Observation, Cite?) +
  ownership-gated `BuildCritiqueService.CritiqueForUserAsync(snapshotId, userId, ct)` (resolve owned snapshot or throw —
  verbatim message, ship-blocker negative test). THREE findings: **(A) untaken choices** — edition-pinned (Edition2014)
  class-entity lookup (mirrors LevelUpAdviceService) + `ClassFeatureRefParser`: subclass-not-chosen (earliest level a
  classFeature name contains `SubclassTitle` passed + Subclass empty) + missing features up to class level,
  `EntityNameIndex.Normalize`-matched vs sheet.Features, each CITED to the real class rule; **(B) stat consistency** —
  computed DIRECTLY from sheet+primitives (DC=8+PB+castMod, attack=PB+castMod, slots via
  `MulticlassSlotTableSeeder.SlotsForCasterLevel(MulticlassSpellcasting.ResolveSlotSource(classes))`) vs recorded
  SpellSaveDC/SpellAttackBonus/SpellSlots — NOT the private string-returning `ResolveForSheetAsync`; **(C) ability
  alignment** — `MulticlassSpellcasting.SpellcastingAbility(class)` vs highest ability (non-casters skipped). Surfaces:
  `critique_build(heroSnapshotId)` per-user chat tool (ownership-gated, closes over session userId, BOTH guard tests) +
  HeroDetail "Review this build" card + `?prompt=` chat hand-off. NO http/mcp/migration → no `.http`/`.insomnia`.
  **LIVE PLAYWRIGHT PASSED** (app image rebuilt): Bruenor (Ranger 4, owned by `test`) → grounded level-accurate critique
  (subclass-not-chosen Ranger Archetype due L3 + missing Favored Enemy/Natural Explorer L1, Fighting Style/Spellcasting L2,
  Primeval Awareness L3, ASI L4 all cited "Ranger (PHB)"; correctly OMITS Extra Attack L5 + TCE variants; NO stat finding =
  DC/atk/slots matched = not over-firing) + ability alignment (Wisdom 14 not highest, Dex 15); no h-overflow desktop 1280 +
  mobile 390; hand-off routes to chat with prompt prefilled. **FINAL-REVIEW BUG (fixed in-loop `ac8eda1`):** single-class
  Warlock recorded pact slots → false "slots don't match" because `ResolveSlotSource` excludes Warlock/pact from the
  standard slot table (all-zero computed) → now SKIP the slot finding when slot source is "none"/pact (+ Warlock regression
  test). Root pattern → dev-flow: a NEW computation branching on a domain classification ships its excluded branch untested
  because the single-fixture smoke walks one branch (twice-confirmed: slice-A d8-fabrication + this) — new gate + red flag
  added. Deferred Minors: unfilled-caster all-zero slots fires (ACCEPTED — truthful); Strengths unrendered (future);
  inline hand-off-link dup (mirror-by-instruction). **CHARACTER-COACH COMPLETE — A (level-up) + B (concept-recommender) +
  C (build-critique) all shipped; no remaining slices.**
- **Encounter design (slice 1)** ✅ DONE (archived `encounter-design`, 2026-07-09; 12 code commits
  `983907e..99c9feb` + integration test `a97c57e`, base 45649f9; build 0/0, FULL suite **1117/1117**).
  FIRST shipped companion-reasoning surface. ONE deterministic math core shared by rate + build so they
  never disagree. `Features/Encounters/`: **`EncounterMath`** (pure, both editions — CR→XP 0..30, 2014
  per-level thresholds × count-multiplier w/ party-size shift, 2024 flat budgets no multiplier; 2024
  table verified vs Roll20 authoritative). **`EncounterAssessor`** (rate: party+monsters→band + context
  boundaries). **`EncounterGenerator`** (build: greedy to target BAND, bounded MaxMonsters=15, overshoot
  guard, sparse fallback flagged; returns the Assessor's verdict → build==rate GUARANTEED; default CR
  ceiling scales to the target band's budget; explicit maxCr/minCr cross-clamped). **`EntitySearchMonsterSource`**
  (real monster retrieval via `IEntityRetrievalService.SearchDiagnosticAsync`, CR from Fields).
  **`EncounterDesignService`** (party from caller's campaign heroes, OWNERSHIP-gated via
  `CampaignRepository.GetByIdAsync(id,userId)` → foreign campaign throws, no leak; explicit partyLevels
  override; empty-campaign = explicit error). Two per-user chat tools `rate_encounter`/`build_encounter`
  (SEC-08 closure, not on shared-key surface, no HTTP route). `AddDndChat` pulls in `AddEncounters` so
  the DI dep is self-contained. Real-Qdrant integration test proves build==rate end-to-end.
  ✅ SHIPPED **v2 monster-quantity/"N goblins" swarms** (archived `2026-07-12-encounter-swarms`;
  7 commits `1eaae47..9d1827a`, base 09d0175; build 0/0, FULL suite **1265/1265**; final opus review READY TO
  MERGE; live UI smoke passed). Anchor-then-fill boss+minions build via candidate RE-SELECTION (was: select each
  once): first pick = highest-XP-under-target anchor, then fill from candidates `Xp < anchorXp` (falls back to the
  anchor tier for a uniform swarm when none cheaper); flat `IReadOnlyList<MonsterRef>` with REPEATS = quantity (math
  + combat tracker unchanged, each swarm member = its own combatant); `MonsterQuantity(Name,Quantity)` input +
  `MonsterGrouping.Group/Describe` + `BuiltEncounterView` display-only. Rate takes structured `{name,quantity}`
  pairs (resolve once → repeat `Clamp(q,1,100)`); `rate_encounter` param reshaped + `build_encounter` echoes grouped
  counts; `EncounterPanel` renders "N× Name" (chat-tools only — no HTTP/.http/migration). Real-Qdrant swarm
  build==rate integration test (Deadly forces a genuine repeat, `Group…Count>1` gate). STILL DEFERRED: multiple
  co-equal anchors ("3 dragons"/"multiple gods"); a `maxMonsters`/`variety` knob; the source still maps 1 entity→1
  MonsterRef. Corpus note: non-5etools "5e"-versioned content won't match the edition filter
  (corpus-data); live chat-driven smoke needs Ollama.

- **Setting-aware lore synthesis** ✅ DONE (archived `2026-07-13-setting-aware-lore`; code commits
  `829849e..1c6904a` incl. edition-default fix `eb9a65f` + DATA-INVARIANT catalog fix `1c6904a`, base 02485db;
  build 0/0, FULL suite **1280/1280**; final opus review READY TO MERGE, one Minor grounding fix in-loop; **LIVE
  SMOKE PASSED with real Eberron lore**). FIRST setting-aware surface — a campaign IS set in a world; lore answers
  scope to that world's SOURCE BOOKS. `Features/Lore/`: `SettingCatalog` (setting→source-book set ∪ core; generic/
  unknown→empty=unscoped; KEYS are the real `dnd_blocks.source_book` DISPLAY NAMES not 5etools keys — DATA
  INVARIANT below) + `SettingLoreService.AskForUserAsync` (ownership-gated `GetByIdAsync(id,userId)`→throw-BEFORE-
  retrieval; resolve setting→`RetrievalQuery.SourceBooks`; cited passages / explicit-empty; NO LLM call — persona
  synthesizes) + `SettingLoreResult`/`CitedPassage`. `Campaign.Setting` nullable (additive migration
  `AddCampaignSetting`) + setting `<select>` on create form AND CampaignDetail (`SetSettingAsync` ownership-scoped).
  `RagRetrievalService` gained a MULTI-SOURCE-BOOK OR filter (`RetrievalQuery.SourceBooks`→Qdrant `should`; single/
  empty back-compat; MODIFIED `rag-retrieval` spec). `ask_setting_lore(campaignId,question,edition?)` per-user chat
  tool (session-userId closure; BOTH guard families; edition defaults NULL=no filter, mirrors search_lore).
  Real-Qdrant non-vacuity scoping test (seed ERLW + off-setting VGM, identical embedding vectors → only the filter
  excludes VGM; mutation-verified RED). **PHASE 1 (data, live 2026-07-13):** registered + ingest-blocks ERLW
  (`Eberron: Rising from the Last War`, book id=4, Setting) → 4322 chunks; admin key sourced from the running
  container UNPRINTED. **DATA INVARIANT CAUGHT+FIXED:** `dnd_blocks.source_book` = book DISPLAY NAMES
  (`Monster Manual 2014`, `Eberron: Rising from the Last War`) NOT 5etools keys — `sourceBook=MM` matched 0; catalog
  fixed to real displaynames (`1c6904a`), verified live (new dev-flow lesson: value-filter constants must equal the
  real corpus payload — a self-seeded test passes with the EXPECTED value even when prod stores a DIFFERENT one).
  **LIVE SMOKE:** Eberron campaign (id 3) setting persists (create+detail); chat "Dragonmarked Houses" →
  `ask_setting_lore` → grounded answer CITED to ERLW (Welcome to Eberron p.22, Ch.1 p.42, Ch.4 p.64; House Jorasco
  Mark of Healing, House Lyrandar Mark of Storms = real ERLW content); no h-overflow desktop+mobile. **FIRST
  LIVE-PASSING chat-driven tool smoke** (prior chat-tool smokes were deferred for Ollama). DEFERRED: lore-answer UI
  panel (chat-only); entity-side setting scoping; catalog grows per ingested setting book (only Eberron today);
  ingest-blocks is ASYNC (HTTP 202 → poll status; do NOT restart app mid-job).

- **Rules adjudication (`ask_rules`)** ✅ DONE (archived `2026-07-13-rules-adjudication`; code commits
  `a715674`/`88c3bdd`/`7e4847a` + incidental setting-aware test fix `29624cb`, base 01115cf; build 0/0,
  FULL suite **1285/1285**; final opus review READY TO MERGE, no findings; **LIVE SMOKE PASSED with real
  rules**). Answers rules questions (incl. multi-rule interactions) with a grounded CITED ruling. Mirrors
  the setting-aware `SettingLoreService` pattern MINUS OWNERSHIP: `Features/Rules/` — `RuleSources`
  (fixed core-rulebook display-name set `{"PlayerHandbook 2014","Dungeon Master's Guide 2014"}` + `TopK=10`
  so both sides of an interaction land) + `RulesAdjudicationService.AskAsync(question, edition?, ct)` (NO
  campaignId/userId; scope via the shipped `RetrievalQuery.SourceBooks` OR filter → cited passages → NO LLM
  call) + `RulesRulingResult` (reuses `Features/Lore.CitedPassage`). `ask_rules(question, edition?)` per-session
  chat tool — OWNERSHIP-FREE (guard test asserts schema exposes NEITHER userId NOR campaignId), edition
  defaults null. Contract: compose only from returned passages, NAME+CITE each rule, flag RAW-vs-DM-call,
  honest "rules don't cover it" on empty — never invent. Real-Qdrant non-vacuity test (seed PHB rule +
  off-scope Monster Manual block, identical embedding vectors → only the source-book filter excludes MM;
  mutation-verified RED). DATA INVARIANT verified live (grappling/prone/cover rules ARE `PlayerHandbook 2014` =
  in `RuleSources`; no fix needed this time). **LIVE SMOKE:** chat "can I grapple a creature that's already
  prone?" → grounded ruling NAMING Prone + Grappling, CITED to PHB (Appendix A Conditions, "Making an Attack"),
  flagging "the rules do not explicitly prohibit" (RAW). NO UI/http/mcp/migration. First shipped surface of the
  "fresh companion-reasoning brainstorm" menu (NPC gen / session prep / rules / downtime — user picked rules).
  **✅ V2 MULTI-HOP SHIPPED (archived `2026-07-13-rules-adjudication-multihop`; commits `2fe6b5a`/`a11f955`/
  `f6f2de5`, base 06ec307; build 0/0, FULL suite **1289/1289**; final opus review READY TO MERGE, no findings;
  LIVE SMOKE PASSED — demonstrably deeper).** `ask_rules(question, ruleTopics?, edition?)` gained optional
  multi-hop: the chat LLM names the distinct rules and passes `ruleTopics`; the service runs ONE scoped
  retrieval PER topic at `RuleSources.TopicTopK=5` → per-topic `RuleTopicPassages` groups + a deduped flat
  `Passages` union (dedup by (Text,SourceBook,Section) max Score; groups retain each passage). Additive result
  shape (`RulesRulingResult.Topics`); single-shot (no topics) = byte-equivalent v1. No new LLM call (the chat
  LLM decomposes). Real-Qdrant multi-hop non-vacuity test (grappling+prone blocks + off-scope MM, identical
  vectors → only the SourceBooks filter excludes MM). Live smoke: "prone then grappled → stand up?" → the LLM
  decomposed into Prone Condition + Grappled Condition + action economy + Mobile feat, each PHB-cited (p.201,
  p.164), GROUNDING the Grappled-condition movement rule that v1 single-shot missed. **Multi-CATEGORY scoping
  REJECTED** by live probe: `category=Rule` returns Monster Manual "Swallow" abilities (noisy tagging); only
  Combat/Condition are clean and each rule maps to a different category, so category-filtering would risk
  DROPPING a rule — multi-hop over the reliable source-book scope is the robust win.
  **REGRESSION LESSON:** the setting-aware DATA-INVARIANT catalog fix (`1c6904a`) had broken 2 SettingLore
  tests asserting old `ERLW`/`PHB` values — missed because the finish-step ran only the `SettingCatalogTests`
  filter, not the full suite; caught by this feature's Task-2 full run, fixed `29624cb`. New dev-flow red flag:
  re-run the FULL suite after ANY value/constant-changing fix. DEFERRED (v2): multi-hop rule decomposition;
  multi-CATEGORY filter (`{Rule,Combat,Condition,Adventuring}`) for finer precision than source-book scoping.

## COMPANION REASONING — QUEUED surfaces (user-requested 2026-07-13, on the fresh-brainstorm menu)
Explicitly queued next surfaces (each its own brainstorm→propose→plan→SDD slice, reusing the retrieve→
cited→persona-synthesize pattern):
- **NPC / statblock generation** — ✅ SHIPPED 2026-07-13 (archived `2026-07-13-npc-generation`; commits
  `09f7d65`/`8bdb9f1`/`3ca28b4`, base 3caf153; build 0/0, FULL suite **1296/1296**; final opus review READY TO
  MERGE, 3 accepted Minors; LIVE SMOKE PASSED). `generate_npc(concept, archetype, maxCr?)` ownership-free chat
  tool: the LLM picks the archetype, `NpcGenerationService` resolves it by EXACT name (anti-fuzzy — a non-matching
  top hit is rejected, tested adversarially with Giant-Spider-vs-Spy) + gates by maxCr, returns a grounded
  `NpcStatBlock` (Cr via `MonsterCr`, Hp/abilities via `MonsterFields` JsonSerializerDefaults.Web, + `CanonicalText`
  via `GetByIdAsync` as the authoritative base), miss/over-CR → `ArchetypeInCorpus=false` + `NpcArchetypes.Common`
  roster → LLM re-picks. Mirrors `BuildRecommenderService`. `Features/Npc/`. NO LLM call, NO ownership/migration/
  http/mcp. Real-Qdrant grounding integration test (seeded Spy → grounded; bogus → roster; anti-fuzzy genuinely
  exercised end-to-end via same-vector stub). LIVE SMOKE: "shifty Sharn dockworker" → LLM picked **Commoner** →
  tool returned REAL stats (CR 0, HP 4, AC 10, all-10 abilities, club 1d4) → persona invented name (Jaren)/
  personality/Sharn-Merchant-Guild hook. **Grounding contract CONFIRMED: mechanical stats REAL, only flavour
  invented.** NOTE: `dnd_entities.sourceBook` = 5etools key ("MM"), cited as-is. Accepted Minors: maxCr fails-open
  on unparseable CR (theoretical); TopK:1+exact-match recall tradeoff (by design); empty base if GetByIdAsync
  misses (low risk). DEFERRED (v2, feeds session-prep): setting-aware NPC names/hooks (via `ask_setting_lore`);
  tool-assembles-full-NPC; party/group of NPCs. HISTORICAL: Generate an NPC
  GROUNDED by anchoring to a REAL corpus stat block. FEASIBILITY CONFIRMED: the MM NPC roster exists as Monster
  entities (Guard/Spy/Noble/Commoner/Bandit/Cultist/Priest/Mage/Veteran/Thug/Acolyte/Bandit Captain/Knight/Scout/
  Assassin, all real stats). NOTE: `dnd_entities.sourceBook` = 5etools KEY ("MM"/"PHB"), UNLIKE `dnd_blocks` display
  names. **DESIGN (chosen):** mirrors `recommend_build` — `generate_npc(concept, archetype, maxCr?)` (NOT
  ownership-gated): the LLM picks the stat-block archetype fitting the concept; the service VALIDATES it exists as a
  Monster entity + returns its REAL stat block (numbers cited); not-found → available NPC archetypes → LLM re-picks;
  the PERSONA composes the reskin (name/personality/appearance/hook) around the grounded stats — mechanical numbers
  REAL, only flavor invented. Standalone, SINGLE NPC per call. DEFERRED (v2): setting-aware (draw names/hooks from
  the campaign's setting lore via `ask_setting_lore`); tool-assembles-full-structured-NPC; party/group of NPCs;
  these feed the session-prep composition surface.
- **Session prep** — COMPOSE the already-shipped surfaces: an encounter (`build_encounter`) + a themed NPC +
  setting lore hooks (`ask_setting_lore`) into one grounded prep packet for a campaign. Highest DM value,
  broadest scope (orchestration layer); best once NPC-gen exists.
- **Downtime / crafting** — plan downtime activities grounded in the rules. FEASIBILITY: the rich downtime/
  crafting rules live in **XGE (Xanathar's), which is NOT ingested** — only DMG basic downtime is in the corpus;
  needs an XGE Phase-1 ingest first (register + ingest-blocks, like ERLW) to be more than thin.
- **Rules-adjudication v2** — ✅ SHIPPED 2026-07-13 (`2026-07-13-rules-adjudication-multihop`): multi-hop
  per-topic grounding via `ruleTopics`. Multi-CATEGORY filter REJECTED (category tagging noisy — `category=Rule`
  returns monster prose). See the rules-adjudication entry above. Remaining queued surfaces: NPC-gen,
  session-prep, downtime/crafting (XGE ingest first).

## COMPANION UX / TABLE-PLAY — all SHIPPED (user-requested 2026-07-09/10)
- **Item A — Dice roller** ✅ DONE (archived `dice-roller`, 2026-07-09; commits `4c25566..623b9a7`; full
  suite 1143/1143). `Features/Dice/`: `DiceExpression.TryParse/Parse` (NdX±K, all 7 dice, d20-only adv/dis;
  never throws — oversized count/modifier + MaxModifier 1000 rejected cleanly); `IRandomSource`/
  `SystemRandomSource` (only nondeterminism, seedable); `DiceRoller.Roll → RollResult` (adv=max/dis=min two
  d20, exact breakdown string). `CompanionUI/Components/DiceRollerPanel.razor` embedded on CampaignDetail
  (quick-die buttons + count + modifier + adv/dis + free-text; ephemeral recent list; no-throw-to-circuit).
- **Item B — Campaign roll+encounter HISTORY with hidden/reveal** ✅ DONE (archived `campaign-log-history`,
  2026-07-10; commits `c9f027d..59412ce`; full suite **1153/1153**). ONE unified `CampaignLogEntry` table
  (Kind Roll|Encounter + JSON PayloadJson, Label, Hidden, campaign+user scoped) + EF migration (additive,
  cascade-deleted with the campaign). `CampaignLogRepository` — ALL reads/commands 3-key ownership-scoped
  (Id/CampaignId/UserId); reveal/delete on a foreign entry = 0 rows (proven by real-Postgres negative test).
  Rolls AUTO-LOG on every roll with an optional label (skill/save/attack/damage quick-picks); encounters
  EXPLICIT-save via a new `EncounterPanel` (build via `EncounterDesignService.BuildForUserAsync`, save +
  hidden checkbox). `CampaignLog` timeline component (newest-first, hidden badge + Reveal + Delete,
  null-safe render). `_userId` from the authenticated NameIdentifier claim; page ownership gate
  `GetByIdAsync(id,userId)` + redirect. FOLLOW-UPS SHIPPED (2026-07-10): encounter payload PartyLevels now
  populated (commit `87fdbd0` — `BuiltEncounter.PartyLevels` threaded through `EncounterGenerator`, saved by
  `EncounterPanel`); **live UI smoke of roll→log→reveal PASSED** (fresh local build vs real Postgres+Qdrant,
  Playwright: d20 labelled "Deception" auto-logged + persisted across reload; hidden encounter row rendered
  hidden badge + Reveal → click un-hid it in UI and flipped `Hidden`→false in Postgres; EncounterPanel
  empty-campaign error path graceful). DEFERRED minor: reveal/delete not try-wrapped (matches DeleteNote posture).
- **Item C — Persisted combat/initiative tracker + dedicated play page** ✅ DONE (archived
  `combat-initiative-tracker`, 2026-07-10; 16 commits `b8492c3..687b4ee`; full suite **1179/1179**; final
  whole-branch review on opus = READY TO MERGE, all 7 cross-path invariants held). New `Features/Combat/`
  slice: `Combat`+`Combatant` two-table relational model (additive migration), `Condition` enum (fixed 15,
  edition-independent, stored as `ConditionsJson`), `CombatRepository` (ALL commands 3-key ownership-scoped;
  one-active-combat guard; ended-combat history), `CombatService` (draft party from heroes / draft monsters
  with auto-rolled init via `DiceRoller`+seeded `IRandomSource` / manual add; `EndCombatAsync` = DM-approval
  write-back of post-fight HP as a NEW append-only `HeroSnapshot` per linked hero + a `Combat`-kind
  `CampaignLogEntry` breadcrumb). **IDENTITY-based turn tracking** (`CurrentTurnCombatantId`, NOT a positional
  index — the review caught that a positional index drifts when the UI re-sorts on remove/add/init-edit; fixed
  by task 12b). New `CompanionUI/Pages/Campaigns/CampaignTable.razor` at `/campaigns/{id}/table` hosts the
  dice roller + encounter panel + `InitiativeTracker` + campaign log (all MOVED off `CampaignDetail`, which now
  links "▶ Run session"). `EncounterPanel.OnBuilt` feeds built monsters to the tracker; editable per-combatant
  MaxHp so encounter-drafted monsters (MonsterRef carries no HP) are trackable. NO HTTP/MCP surface (server-side
  Blazor). **DEFERRED FOLLOW-UPS SHIPPED (2026-07-10c, commits `9c21f27..25e9de9`, suite 1186/1186):** D1 =
  DB filtered-unique index `IX_Combats_CampaignId_ActiveUnique` (one active combat per campaign, backstops the
  StartAsync race → returns null on DbUpdateException) + `EndCombatAsync` batches the party load (N+1 killed);
  D3 = monster initiative modifier from entity Dexterity (`MonsterFields.dex` → `floor((dex-10)/2)`, threaded
  through `MonsterRef.InitiativeModifier`, tracker stays Qdrant-free); D4 = **live Playwright smoke PASSED**
  (rebuilt dev container from current main: play page renders all 4 components, CampaignDetail relocated,
  start→manual-monster-auto-init→editable-MaxHp→condition-toggle→HP−→PERSISTS-across-reload→advance-wraps-round→
  end-with-approval→history→breadcrumb-rendered); D5 = smoke FOUND+FIXED a live bug (ending a combat didn't
  refresh the on-page CampaignLog — added `InitiativeTracker.OnLogChanged`→`RefreshLog`, re-smoked green).
  STILL DEFERRED (minor UX): removing the CURRENT combatant leaves no highlight until the next advance (which
  re-anchors to top, not next-after-removed).

## TABLE-PLAY v2 — COMPLETE (all 4 slices SHIPPED, user-requested 2026-07-11)
- **`combat-fight-fidelity`** ✅ DONE (archived `2026-07-11-combat-fight-fidelity`; 6 commits `5ec4741..6ee7466`;
  build 0/0, full suite **1196/1196**; final opus review READY TO MERGE, no findings). "Run a real fight" slice:
  (a) **Monster auto-HP** — encounter-drafted monsters arrive with real MaxHp from the stat block (book average
  by default, or app-rolled from `hp.formula` via a "🎲 Roll monster HP" toggle), the twin of the shipped monster-Dex
  path: `MonsterRef` gained `AverageHp`/`HpFormula`, read via `MonsterHp.TryRead` at the 3 construction sites,
  consumed by `DraftMonstersAsync(..., bool rollHp)`. (c1) **Damage/heal-by-N** — the combatant HP row's ±  buttons
  apply a per-row N (default 1 = old behavior), reusing the clamped `AdjustHpAsync`. (c2) **Remove-current turn
  fix** — `RemoveCombatantAsync` re-anchors `CurrentTurnCombatantId` to the next-in-order (wrap/null) when the
  acting combatant is removed (was the deferred Item C bug); made ATOMIC via the execution-strategy transaction +
  tracker-free ExecuteUpdate/ExecuteDelete (the review cited the existing dev-flow gate — it WORKED). Spec delta
  also re-synced the stale `CurrentTurnIndex`→`CurrentTurnCombatantId` drift. NO migration/schema/http/mcp. Live
  smoke: toggle renders, 11-damage-in-one-click, remove-current re-anchors + illuminates the next.
- **`combat-condition-durations`** ✅ DONE (archived `2026-07-11-combat-condition-durations`; 6 commits
  `815c731..6d7a7ee`; build 0/0, full suite **1201/1201**; final opus review READY TO MERGE, no findings).
  Table-play v2 slice 2 = **conditions with duration**: each combatant condition optionally carries a
  per-condition/per-combatant rounds-remaining (`ConditionTimer(Condition, int? RoundsRemaining)`; null =
  indefinite), stored in the SAME `ConditionsJson` column (JSON SHAPE change, NO migration; backward-compat
  deser reads old string-array as indefinite). On a round ROLLOVER (`AdvanceTurnAsync` wrap branch), every
  combatant's timed conditions decrement by 1 and expire at 0 (indefinite never tick) — inside the existing
  single atomic `SaveChangesAsync`. UI: each active chip has a small rounds field (empty = ∞). The retype
  (`Conditions`→timers across helper/UpdateCombatantAsync/razor) landed ATOMICALLY in Task 1 (behavior-preserving,
  green) then tick + UI on top — now a dev-flow gate. Live smoke: set Poisoned=2 → Round 2 ticks to 1 → Round 3
  auto-expires. TABLE-PLAY v2 REMAINING: (d) a global non-campaign scratch dice/encounter surface.
- **`combat-tie-reorder`** ✅ DONE (archived `2026-07-11-combat-tie-reorder`; 6 commits `bffb29d..a00f2fc`;
  build 0/0, full suite **1209/1209**; final opus review READY TO MERGE). Table-play v2 slice 3 = **manual
  reorder for initiative ties**: ▲/▼ on a combatant row reorders it among others the sort treats as tied
  (equal `InitiativeRoll`/`InitiativeModifier`/side) by SWAPPING their `AddedOrder` — reuses the existing
  column, NO migration. `CombatantOrder.AreTied(a,b)` (the above-`AddedOrder` equality) gates BOTH the repo
  swap and the UI enable/disable (a ▲/▼ is enabled iff the swap would reorder). `CombatRepository.MoveCombatantAsync`
  (ownership-scoped, atomic one SaveChanges, no-op on edge/non-tie/foreign; current turn is identity-based →
  untouched). Review-hardened: `AddedOrder` now assigned `max+1` (was `Count`) so it never collides after a
  remove-then-add. Live smoke: two combatants tied at 19 → ▲/▼ enable exactly (A ▼-only, B ▲-only, Kobold both
  off) → click swaps [A,B]→[B,A], non-tied Kobold unaffected.
- **`scratch-surface`** ✅ DONE (archived `2026-07-11-scratch-surface`; 3 commits `59d95cb`(spec)/`90bdc3b`/`49ab672`
  + archive `5a85b0e`; build 0/0, full suite **1209/1209** unchanged = behavior-neutral; final opus review READY
  TO MERGE, no findings). Table-play v2 slice 4 (LAST) = **global non-campaign scratch dice/encounter surface**.
  Pure Blazor wiring, NO new domain/persistence/migration/http/mcp. New `CompanionUI/Pages/Scratch.razor` at
  `/scratch` (`[Authorize]`, InteractiveServer, `_userId` from NameIdentifier — copied from CampaignTable) +
  a "🎲 Scratchpad" `MainLayout` NavLink (new `scratch-surface` capability spec + MODIFIED `sidebar-navigation`
  delta enumerating the 4th link). REUSES two already-shipped+tested paths verbatim: `DiceRollerPanel CampaignId="0"`
  (its `if(CampaignId>0)` auto-log guard = purely ephemeral off-campaign) and `EncounterDesignService.BuildForUserAsync`
  explicit-`partyLevels` path (`ResolvePartyAsync` returns partyLevels when `Count>0`, never touching a campaign).
  The page's ONLY new logic: a size/level party input to `_partyLevels = Enumerable.Repeat(Clamp(level,1,20),
  Clamp(size,1,10)).ToList()` (clamp guarantees non-empty so the empty-party throw is unreachable). `EncounterPanel`
  gained ONE optional `[Parameter] IReadOnlyList<int>? PartyLevels` (passed to the service instead of hard-coded null;
  null = unchanged campaign behavior) + its save row wrapped in `@if (CampaignId > 0)` (hidden off-campaign) =
  behavior-neutral for the campaign table page. SMOOTHEST slice yet: both tasks task-review-clean on FIRST try (no fix
  loops). Live Playwright smoke PASSED (rebuilt stale 56min-old app image first, else it'd test old code): /scratch
  renders dice+encounter only (no combat/log), Scratchpad nav active (others not), ephemeral d20 roll (no log write),
  size/level build to Giant Spider 200XP with NO save row, no h-overflow desktop(2214)+mobile(375/390); REGRESSION probe =
  Build on a 0-hero campaign table returned "Campaign has no heroes…" = POSITIVE proof the campaign party-path (not the
  explicit-party path) is intact. **dev-flow tightening (skill-optimizer):** the UI validation gate now carries the
  CONCRETE container-rebuild command (`docker compose build app && docker compose up -d app`) + the why (stale image
  silently screenshots old markup). **TABLE-PLAY v2 COMPLETE — no remaining slices.**

## UI / VISUAL DESIGN — SHIPPED (user-requested 2026-07-11)
- **`visual-design-system`** ✅ DONE (archived `2026-07-10-visual-design-system`; 9 commits `d1e633c..fce1d3c`
  + archive `ec2b8e2`; build 0/0, full suite **1186/1186** unchanged = behavior-neutral; final opus review READY
  TO MERGE). Rewrote `wwwroot/app.css` from ad-hoc hex into a **token-driven "arcane console" dark theme**:
  `:root` custom properties (palette base `#0E1018`/surface `#171A27`/border `#2A2F48`/ember-gold `#E8B65A` for
  primary+illumination/arcane-violet `#8B7CF6` for links+focus/hp/heal/muted/text; spacing/radii/shadow/type
  scales), **self-hosted woff2 fonts** (`wwwroot/fonts/`, via `npm pack @fontsource/…` — Grenze Gotisch blackletter
  display for wordmark/h1/combat-name ONLY, Alegreya Sans body, JetBrains Mono data; offline, no CDN), shared
  primitives (`.btn`/`.btn--primary`/`.btn--ghost`/`.btn--danger`, `.card`, `.chip`, `.badge`, styled inputs), and
  every surface restyled (sidebar+auth+campaigns+campaign-detail, the TABLE page + its 4 components, heroes+sheet,
  chat). **Signature = the illuminated initiative rail** (current combatant gets an ember left-edge + glow + ember
  init#) and **conditions collapsed** to active-chips + a "+" popover (killed the wall of 15 buttons). Presentational
  only — NO behavior/route/domain/MCP change (markup edits = classes + pure display helpers `HpColor`/edition-label
  + a view-state popover toggle; existing handlers reused). Responsive (sidebar collapses to a wrapping top bar at
  ≤820px, no horizontal overflow), focus rings, reduced-motion. **NEW dev-flow gate added:** UI/presentational
  changes verify via build+full-suite-green (behavior-neutral) + LIVE Playwright screenshots (desktop+mobile) +
  overflow check + class-resolution grep — unit tests can't see a pixel (this session's 5 real defects were all
  screenshot-only). Razor `text@id` email-heuristic gotcha (`d@die`→`d@(die)`) now a dev-flow red flag.
  FOLLOW-UP (fixed): "Build" button now gold — root cause was base `.btn, button.btn{}` (0,1,1) beating
  `.btn--primary` (0,1,0); fixed by bumping modifiers to `.btn.btn--primary` (0,2,0) — hardens ALL primary
  buttons. Chat bubble alignment was ALREADY correct (assistant left-aligned; the centered max-width column
  fooled a screenshot read — verified via browser_evaluate rects). No open UI defects.

## TEST INFRA (confirmed present)
Real Testcontainers in-repo: `Testcontainers.PostgreSql` 4.12.0 (`Persistence/PostgresFixture.cs`,
postgres:18-alpine) + Respawn 7.0.0 per-test isolation; `Testcontainers.Qdrant` 4.12.0
(`VectorStore/Entities/QdrantFixture.cs`). Full `dotnet test` needs Docker. Grounding now HAS real-Qdrant
integration tests (Tier1 scoping + reground round-trip, 2026-07-09b) — GUID-unique collections per test
(the older `QdrantEntityVectorStoreScrollTests` still uses a fixed collection name; safe with 1 fact but
GUID-suffix it if a sibling test is added). Only the real-Ollama JUDGE path stays smoke-only.

## MODEL / INFERENCE UPGRADE PATH (MoE) — INVESTIGATION ⬜ (agreed 2026-06-27; research only, no config drafted)
Cross-cutting: the local model drives BOTH extraction AND the grounding cascade's Tier 2 judge (currently
dense `qwen3:8b` via Ollama, `OllamaOptions.ChatModel`) — and would drive the companion's reasoning too.
**DESIGN STANCE (user):** single-user, LOCAL, personal tool → **latency is a non-issue**, so a
slower-but-stronger local **MoE** can serve everything, staying 100% local ($0, private, no API/egress).
The hard cap is the **8GB VRAM ceiling** (RTX 5070 Laptop), NOT latency.
- **Item 5 — Local MoE upgrade (the real path for qwen3:8b):** **Gemma 4 26B A4B** (Google, Apr 2026,
  Apache-2.0, MoE 26B total / 4B active, multimodal, 256K ctx) or **Qwen3-30B-A3B / Qwen3.6-35B-A3B** (MoE).
  Fit on 8GB via llama.cpp **`--cpu-moe`** (park routed experts in system RAM; attention+shared-expert on
  GPU) + **TurboQuant/`turbo3`** KV-cache compression (DeepMind, ICLR 2026, ~3-bit KV, ~75% VRAM cut).
  Likely move OFF Ollama → **`llama-server`** for those flags. NOTE: plain `qwen3:30b-a3b` was tried
  directly and ABANDONED (5h for 15 entities on 8GB — ran mostly in system RAM); `--cpu-moe` + turbo3 is
  the fix that makes the 26B-MoE tier viable. All-local ceiling = **Gemma-4-26B-A4B**, NOT GLM-5.2.
- **Item 6 — Cloud reasoning backend (OPTIONAL, hybrid only):** **GLM-5.2** (Z.ai, MIT, MoE ~744B total /
  40B active, 1M ctx, ~1% behind Opus 4.8 on agentic; 744B does NOT fit 8GB at any speed → paid CLOUD API
  only: z.ai/Together/OpenRouter, metered ~1/5–1/10 frontier $/token → likely a few $/mo at single-user
  volume). ONLY as the companion's agentic/reasoning backend if the local 26B-MoE proves too weak for
  recommendation turns. Decision: **all-local 26B-MoE for everything ($0, private, default)** vs **hybrid
  local-extract + cloud GLM-5.2 reason (~few $/mo, near-Opus, cloud dep)** — validate on real D&D-rules
  tasks first (coding/agentic benchmarks ≠ rules reasoning). STATUS: research only, user's call.

## LOOSE ENDS / follow-ups
- **markitdown parser candidate** (user-requested 2026-07-12) — evaluate Microsoft `markitdown`
  (https://github.com/microsoft/markitdown) as a PDF→Markdown parser vs Marker/MinerU for the ingestion/extraction
  pipeline; research spike, not a committed slice (`mem:project_markitdown_parser_candidate`, `mem:project/parser_upgrade_mineru`).
- **Published-container Blazor static assets** ✅ FIXED (`8139397`).
- **Qdrant scalar int8 quantization:** shipped + archived. Closed.
- **`extraction-think-mode` spec** ✅ CLOSED — deleted 2026-07-09b (superseded by shipped `/no_think` `803da7b`; the A/B toggle it proposed is moot now the decision is made).
- **DMG Object residuals** (hand-correctable): tighten `StatBlockScanner` / `IsObjectStatBlock`.
- **`dotnet format`** — a comprehensive `.editorconfig` ALREADY EXISTS (18KB, `dotnet_separate_import_directive_groups`, `dotnet_sort_system_directives_first`); the repo had just drifted from it. One-time normalization was applied 2026-07-09b (whitespace/finalnewline/imports; behavior-neutral, build 0/0). `dotnet format --verify-no-changes` clean going forward.
- **Operational live-host smokes** (deferred, need app+Qdrant+Postgres+Ollama up): Item 3 reground
  endpoint (fast pass + `?judge=true`); Item 4 dedup endpoints (duplicates report + compact apply);
  **encounter-design chat build→rate; character level-up chat-driven recommendation (needs Ollama).**
- **Character-coach slices B + C** (planned, on the shipped `Features/CharacterAdvice/` core): **B = concept-to-build
  recommender** (text concept → class/subclass/feat/spell path from scratch); **C = build-critique** (review a sheet
  for strengths/gaps). Each is its own brainstorm→propose→plan→SDD slice reusing the level-up core (deterministic
  delta + cited option menus + ownership-gated per-user orchestrator + chat tool + HeroDetail surface).
- **Level-up grounding coverage — ✅ RESOLVED 2026-07-12 by `fivetools-field-fill`** (see COMPANION REASONING section;
  the 5etools `ImportAll` "path (a)" proof below was UNDONE and replaced by the durable extraction-favored field-fill
  hybrid). Historical diagnosis kept for context:
  (accepted limitation on the shipped level-up slice — user chose ship-as-is 2026-07-11).
  ROOT CAUSE diagnosed 2026-07-11: (1) all 12 PHB classes ARE in `books/canonical/phb14.json` (extraction discovered
  them cleanly); (2) but the CURRENT content-first extraction emits classes as PROSE-ONLY (`fields:{entries:[...]}`,
  no `hd`/`classFeatures`/`subclassTitle`) — the recent spell-recall re-extractions (`764a615`→`0c8e7bd`) regenerated
  phb14.json prose-only; (3) Qdrant `dnd_entities` is STALE — it holds only 4 classes (Bard/Ranger/Sorcerer/Warlock)
  carrying the FULL structured 5etools schema (`hd`/`classFeatures`/`classTableGroups`, `dataSource:llm`) from an OLD
  structured extraction+ingestion never refreshed after the prose regen. So the other 8 classes were never indexed, and
  the 4 that are, are stale-but-structured (why the Ranger live demo worked + Barbarian was absent).
  **Re-running `ingest-entities` does NOT fix it** — it would index 12 PROSE classes with no hit die, which the feature
  correctly skips. The structured class data the feature needs isn't in the current canonical at all. DECISION when
  revisited: **(a) ENRICH via 5etools** vs **(b) RETHINK toward PROSE**. **(a) VERIFIED END-TO-END 2026-07-11:**
  `POST /admin/5etools/import` (`ImportAllAsync`) runs every mapper (`FivetoolsClassMapper`/`Subclass`/`Spell`/`Feat`/…)
  over local `5etools/`, stores the FULL 5etools record as each entity's Fields (`BuildFields => entry.Clone()` →
  hd/classFeatures/subclassTitle/classTableGroups), renders canonicalText (`dispatcher.Render`) so entities are
  retrievable, skips `manual`-sourced entities, upserts to `dnd_entities` (`dataSource:"5etools"`). RAN IT: **9868
  entities mapped+upserted in ~7 min (HTTP 200)**; the level-up card then grounded **all 11 eligible classes with correct
  hit dice** (Ranger d10, Barbarian d12, Fighter/Paladin d10, Sorcerer d6, others d8; Wizard correctly excluded on INT<13)
  — ZERO code changes. So (a) works for ALL classes in one endpoint call. **CURRENT INDEX STATE (2026-07-11):**
  `dnd_entities` is now populated with the whole 5etools corpus (9868, `dataSource:5etools`), which UPSERT-OVERWROTE the
  prior non-manual extraction entities by id (monsters/spells/etc. are now 5etools versions). Reversible (re-run
  ingest-entities / delete by dataSource). **OPEN DECISIONS:** (i) keep 5etools as the entity source of truth (path a) vs
  revert to extraction/prose (path b, north star); (ii) `ImportAll` is unscoped (all sources incl. 2024/XPHB) — a clean
  3-book re-ingest of PROSE entities AFTER this would clobber the structured classes (id collision), so ordering/id
  namespaces must be reconciled. **REFINEMENT ✅ FIXED (commit 3747716):** the level-up class/subclass lookup now pins to
  Edition2014 (`LevelUpEdition` const) — the slot tables + multiclass rules are 2014-only, so class data must be too;
  non-vacuous test (wrong-edition entity first → 2014 still chosen), full suite 1228/1228, all 12 PHB classes confirmed
  to have Edition2014 entities so coverage is unchanged. (b) RETHINK toward PROSE (HP/features from `entries` + LLM) remains the north-star alternative
  (`mem:project_entity_extraction_rethink`).
- **✅ RESOLVED (2026-07-12) — entity source of truth = EXTRACTION; 5etools patches holes.** The
  `fivetools-field-fill` feature SHIPPED the hybrid: extraction owns every entity (99% on core books), and a
  field-level 5etools gap-fill merges ONLY missing allowlisted STRUCTURED fields (Class hd/classFeatures/subclassTitle
  etc.; Spell level/school/range; Monster tags) — never overwriting extraction/`entries`, auto-run after
  extract-entities (deterministic → can't decay), provenance in a reserved `_fivetoolsFilledFields` key. The blunt
  2026-07-11 `POST /admin/5etools/import` (9868 wholesale, `dataSource:5etools`) was UNDONE: filled the 3 books,
  re-ingested, deleted the 8593 `data_source:5etools` strays. `dnd_entities` is now **2307 extraction entities
  (`data_source:llm`) only**, all 12 classes grounded from extraction+fill, Aboleth reads as the extraction version.
  See the shipped feature below (COMPANION REASONING section) and `mem:reference_build_env_gotchas` (Qdrant payload keys).
- **Level-up deferred Minors** (final-review, non-blocking): clamp HP gain to the D&D floor of 1 (very-low-CON edge);
  surface `DipValidity`'s failed-prereq reason instead of only excluding ineligible dips (spec's "identify the failed
  prerequisite" scenario is currently satisfied only by exclusion).

## How we progress (discipline — never skip)
Each item: **superpowers:brainstorming** (full dialogue) → **opsx:propose** → **superpowers:writing-plans**
→ **superpowers:subagent-driven-development** (per-task TDD + reviewer subagents; final whole-branch
review on opus). Work DIRECTLY on main (`mem:workflow/work_on_main`); commit autonomy granted. FINISH on
"commit"/"archive"/"finish X": commit → `openspec archive` → `skill-optimizer` → refresh this roadmap
(`mem:workflow/finishing_a_spec`). `ingest-entities` in the finish step is EXTRACTION/CANONICAL-only —
retrieval/refactor changes skip it (dev-flow SKILL updated Item 4).
PLAN-VS-SPEC lesson: the final whole-branch review catches plan/spec drift; the SPEC governs. It also
catches spec-requirement-not-implemented that per-task reviews miss (Item 3: `Ungrounded` was set but
NOT excluded from `dnd_entities` — spanned 3 write paths — until final review; and judge I/O failure
mislabeled real entities). Cross-path invariants must be traced across ALL paths at final review; inject
INTERFACES not concrete types — both now in dev-flow SKILL.

## Current position (2026-07-13c)
Extraction/retrieval FOUNDATION + **ALL named reasoning items (2,3,4) SHIPPED**; **companion reasoning +
table-play all SHIPPED + archived: encounter-design (slice 1), dice roller (Item A), campaign log history
(Item B), combat/initiative tracker + dedicated play page (Item C).** **UI fully restyled — `visual-design-system`
SHIPPED (token-based "arcane console" dark theme across every surface; the app no longer looks unfinished).**
**Table-play v2 COMPLETE (all 4 slices SHIPPED + archived): slice 1 (`combat-fight-fidelity`), slice 2
(`combat-condition-durations`), slice 3 (`combat-tie-reorder`, tie reorder), slice 4 (`scratch-surface`,
global non-campaign dice/encounter page).** Only active openspec change is the parked
`prose-grounded-knowledge-model`. **COMPANION REASONING surfaces: encounter-design + the COMPLETE character coach
(level-up + concept-to-build recommender + build-critique, all shipped 2026-07-12).** **HYBRID entity model RESOLVED + shipped
2026-07-12 (`fivetools-field-fill`): extraction owns all entities, 5etools field-fill patches missing structured
fields; `dnd_entities` now 2307 extraction-only entities, level-up grounds all 12 classes from extraction+fill.**
**CHARACTER-COACH COMPLETE — A (level-up) + B (concept-to-build recommender) + C (build-critique) all shipped 2026-07-12.** **Encounter-design v2 swarms ALSO shipped + archived 2026-07-12
(`2026-07-12-encounter-swarms`): boss+minions anchor-then-fill build + structured `{name,quantity}` rate,
flat-list-with-repeats representation, grouped display; final opus review READY TO MERGE, live UI smoke passed.**
**SETTING-AWARE LORE SYNTHESIS shipped + archived 2026-07-13 (`2026-07-13-setting-aware-lore`): per-campaign
`Campaign.Setting` → source-book-scoped grounded cited `ask_setting_lore` tool; ERLW ingested (4322 chunks);
LIVE SMOKE PASSED (Dragonmarked Houses answered + ERLW-cited) — the FIRST live-passing chat-driven tool smoke.**
**RULES ADJUDICATION (`ask_rules`) shipped + archived 2026-07-13 (`2026-07-13-rules-adjudication`): grounded
CITED rulings scoped to the core rulebooks, ownership-free; LIVE SMOKE PASSED (grapple-while-prone → ruling
naming Prone+Grappling, PHB-cited, RAW-flagged). + V2 MULTI-HOP shipped (`2026-07-13-rules-adjudication-multihop`):
`ruleTopics` per-rule grounding; live smoke deeper (prone+grappled+action-economy+Mobile, each PHB-cited);
multi-category REJECTED (noisy tagging).**
**NPC/STATBLOCK GENERATION shipped + archived 2026-07-13 (`2026-07-13-npc-generation`): `generate_npc` anchors to a
REAL corpus stat block (anti-fuzzy exact-name), persona invents only flavour; LIVE SMOKE PASSED (dockworker → real
Commoner stats + invented Sharn hook).**
FULL suite **1296/1296**.
NEXT candidates (user's call):
(1) companion REASONING frontier (character-coach + encounter-swarms + setting-aware lore + rules-adjudication (+v2)
    + **NPC generation** ALL DONE): the remaining QUEUED fresh-brainstorm surfaces — **session prep** (compose
    encounter+NPC+lore hooks — the orchestration capstone, now that its atomic pieces exist), or **downtime/crafting**
    (needs an XGE ingest first, like ERLW); OR NPC-gen v2 (setting-aware names/hooks, party of NPCs); OR grow the
    setting catalog (ingest more setting books);
(2) [level-up grounding coverage — ✅ RESOLVED via `fivetools-field-fill` field-fill hybrid; optional: `backfill-spells` for spell gaps];
(3) resume the parked `prose-grounded-knowledge-model` re-architecture (`mem:project_entity_extraction_rethink`);
(4) the **local MoE model upgrade** (MODEL/INFERENCE UPGRADE PATH — Item 5/6) — user DEFERRED this 2026-07-11
    ("leave moe for later"); a foundational lever under all when revisited.
Deferred operational: live-host smokes for Item 3 (reground, Ollama judge path), Item 4 (dedup endpoints),
encounter-design (chat build→rate), Item C (play page + tracker Playwright smoke). Table-play roll→log→reveal
UI smoke DONE 2026-07-10 (see Item B). Relates to
`mem:extraction/dmg_generic_backfill_status`, `mem:project_entity_extraction_rethink`,
`mem:reference_build_env_gotchas`.
