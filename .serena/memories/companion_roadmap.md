# D&D Companion — Roadmap & Progress (living; refreshed 2026-07-05)

**North star:** a companion agent that REASONS (character build / encounter design / setting-aware
lore), not just retrieves. RAG + extraction are the *means*, and that foundation is now largely built —
the remaining north-star work is the REASONING layer (Items 2–4).

## Status legend: ✅ done · 🔄 in progress · ⬜ not started

## FOUNDATION — extraction + retrieval  ✅ (collapsed; see git history + archived changes)
Everything below shipped and is archived — do NOT re-plan it, just build on it:
- **WRITE-layer extraction quality:** recall fix + precision authoritative-allowlist + deterministic
  type resolution; generic 5etools backfill (`fivetools-entity-backfill`, archived 2026-07-04);
  **Object entity type + decline-not-leak** (union-hoist + deterministic `Force(Object)`, archived
  2026-07-05 — `mem:extraction/dmg_generic_backfill_status`). MM/PHB/DMG re-run + backfilled + validated.
- **Extraction perf — qwen3 /no_think:** SHIPPED (`803da7b`), measured live ~8× faster, no
  classification regression, thinking-runaway losses gone. (Was a roadmap perf item — done.)
- **Slice 1 character-fact-resolution:** shipped (`235699a`) — `CharacterResolutionService`,
  structured-fact store, `resolve_character_feature` MCP tool.

## FRONTIER — the REASONING layer (north star; NOT started)
- **Item 2 — Slice 2: multiclass spellcaster** ⬜  ← RECOMMENDED NEXT (first genuinely "reasons not
  retrieves" feature; the deliberate stress test). `CharacterSheet.Level int` → `List<ClassLevel>
  {class,level,subclass}`; combined-caster slot math (full + ½ + ⅓; Warlock Pact Magic separate) →
  one Multiclass Spellcaster table; Bin-C queries FORK single-class vs multiclass. Extends slice-1
  rails. Design home: `openspec/changes/prose-grounded-knowledge-model/design.md §J`.
- **Item 3 — Auto-NeedsReview grounding cascade** ⬜ (own brainstorm→spec). Tier 1 = embedding check
  (reuse mxbai `dnd_blocks` vectors → promote); Tier 2 = qwen3 judge on residual (promote/decline/keep,
  bias decline-when-unsure). Reuse `errorsOnly` as a re-ground pass; can re-audit `declined.json`
  false-drops. Cannot auto-PROVE fine cells (Red→fire) — stay needsReview+provenance.
- **Item 4 — Corpus-wide dedup** ⬜ — NEWLY RELEVANT: the DMG Object work produced exactly the dup
  pattern this targets (Ballista/Cannon each twice: `dmg14.monster.*` + `dmg14.item.*`, distinct ids).
  Dedup by canonical id / `EntityNameIndex` key, OUT of the extraction path (separate, reviewable).

## LOOSE ENDS / follow-ups (small, close before or alongside Item 2)
- **Qdrant scalar int8 quantization:** spec + code shipped (`11c7665`, `qdrant-scalar-quantization`);
  LIVE validation (recall/memory/latency vs float32 baseline — spec tasks 4.2/4.3) PENDING an app rebuild.
- **Spec housekeeping:** fold `/no_think` + `Force(Object)` (both in `803da7b`) into the
  `extraction-think-mode` + `object-entity-type` task lists — implemented ahead of formal spec tasks.
  `extraction-think-mode` spec (config-toggle form) is proposed, not applied.
- **DMG Object residuals** (hand-correctable): 2 Ballista/Cannon dup entities + 2 junk Objects named
  from a "Damage Immunities:" stat-line fragment; tighten `StatBlockScanner` naming / `IsObjectStatBlock`.

## How we progress (discipline — never skip)
Each item: **superpowers:brainstorming** (full dialogue) → **opsx:propose** (spec in
`openspec/changes/<name>/`) → **superpowers:writing-plans** → **superpowers:subagent-driven-development**
(per-task TDD + reviewer subagents; final whole-branch review on opus). Work DIRECTLY on main — no
feature branches (`mem:workflow/work_on_main`); commit autonomy granted. FINISH on "commit": commit →
`openspec archive` → run `skill-optimizer` (`mem:workflow/finishing_a_spec`).

## Current position (2026-07-05)
Extraction/retrieval FOUNDATION is complete and mature (Object type + /no_think last night). Tree clean,
all changes committed + pushable. The frontier is the reasoning layer — **Item 2 (multiclass)** is the
recommended next build. Relates to `mem:extraction/dmg_generic_backfill_status`,
`mem:project_entity_extraction_rethink`, and the fuller file roadmap `mem:project_companion_roadmap`.
