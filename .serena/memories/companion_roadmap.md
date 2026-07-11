# D&D Companion — Roadmap & Progress (living; refreshed 2026-07-11)

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
  DEFERRED: **v2 monster-quantity/"N goblins" swarms** (own spec — generator selects each candidate once,
  source maps 1 entity→1 MonsterRef); non-5etools "5e"-versioned content won't match the edition filter
  (corpus-data); live chat-driven smoke needs Ollama.

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
- **Published-container Blazor static assets** ✅ FIXED (`8139397`).
- **Qdrant scalar int8 quantization:** shipped + archived. Closed.
- **`extraction-think-mode` spec** ✅ CLOSED — deleted 2026-07-09b (superseded by shipped `/no_think` `803da7b`; the A/B toggle it proposed is moot now the decision is made).
- **DMG Object residuals** (hand-correctable): tighten `StatBlockScanner` / `IsObjectStatBlock`.
- **`dotnet format`** — a comprehensive `.editorconfig` ALREADY EXISTS (18KB, `dotnet_separate_import_directive_groups`, `dotnet_sort_system_directives_first`); the repo had just drifted from it. One-time normalization was applied 2026-07-09b (whitespace/finalnewline/imports; behavior-neutral, build 0/0). `dotnet format --verify-no-changes` clean going forward.
- **Operational live-host smokes** (deferred, need app+Qdrant+Postgres+Ollama up): Item 3 reground
  endpoint (fast pass + `?judge=true`); Item 4 dedup endpoints (duplicates report + compact apply).

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

## Current position (2026-07-11)
Extraction/retrieval FOUNDATION + **ALL named reasoning items (2,3,4) SHIPPED**; **companion reasoning +
table-play all SHIPPED + archived: encounter-design (slice 1), dice roller (Item A), campaign log history
(Item B), combat/initiative tracker + dedicated play page (Item C).** **UI fully restyled — `visual-design-system`
SHIPPED (token-based "arcane console" dark theme across every surface; the app no longer looks unfinished).**
**Table-play v2 COMPLETE (all 4 slices SHIPPED + archived): slice 1 (`combat-fight-fidelity`), slice 2
(`combat-condition-durations`), slice 3 (`combat-tie-reorder`, tie reorder), slice 4 (`scratch-surface`,
global non-campaign dice/encounter page).** Only active openspec change is the parked
`prose-grounded-knowledge-model`. FULL suite **1209/1209**.
NEXT candidates (user's call):
(1) more companion REASONING surfaces: **encounter-design v2 swarms** ("N goblins" — generator + monster
    source emit per-monster quantities), setting-aware lore synthesis, deeper character-build advice;
(2) [table-play v2 is now COMPLETE — no remaining slices];
(3) resume the parked `prose-grounded-knowledge-model` re-architecture (`mem:project_entity_extraction_rethink`);
(4) the **local MoE model upgrade** (MODEL/INFERENCE UPGRADE PATH — Item 5/6), a foundational lever under all.
Deferred operational: live-host smokes for Item 3 (reground, Ollama judge path), Item 4 (dedup endpoints),
encounter-design (chat build→rate), Item C (play page + tracker Playwright smoke). Table-play roll→log→reveal
UI smoke DONE 2026-07-10 (see Item B). Relates to
`mem:extraction/dmg_generic_backfill_status`, `mem:project_entity_extraction_rethink`,
`mem:reference_build_env_gotchas`.
