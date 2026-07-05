# D&D Companion — Roadmap & Progress (living; refreshed 2026-07-05)

**North star:** a companion agent that REASONS (character build / encounter design / setting-aware
lore), not just retrieves. RAG + extraction are the *means*, and that foundation is now largely built —
the remaining north-star work is the REASONING layer (Items 3–4; Item 2 is now DONE).

## Status legend: ✅ done · 🔄 in progress · ⬜ not started

## FOUNDATION — extraction + retrieval  ✅ (collapsed; see git history + archived changes)
Everything below shipped and is archived — do NOT re-plan it, just build on it:
- **WRITE-layer extraction quality:** recall fix + precision authoritative-allowlist + deterministic
  type resolution; generic 5etools backfill (`fivetools-entity-backfill`, archived 2026-07-04);
  **Object entity type + decline-not-leak** (union-hoist + deterministic `Force(Object)`, archived
  2026-07-05 — `mem:extraction/dmg_generic_backfill_status`). MM/PHB/DMG re-run + backfilled + validated.
- **Extraction perf — qwen3 /no_think:** SHIPPED (`803da7b`), ~8× faster, no classification regression.
- **Slice 1 character-fact-resolution:** shipped (`235699a`) — `CharacterResolutionService`,
  structured-fact store, `resolve_character_feature` MCP tool.

## FRONTIER — the REASONING layer (north star)
- **Item 2 — Slice 2: multiclass character** ✅ DONE (archived `multiclass-character`, 2026-07-05;
  18 commits `057e7e7..db16979`, 992/992 tests, build 0/0). GENERAL multiclass (any combo, caster or
  not — user was emphatic "not only spellcaster"): `CharacterSheet.Classes: List<ClassLevel>` source of
  truth + derived flat fields + tolerant legacy-JSON migration (set-only STJ sinks — `[JsonExtensionData]`
  shadowing was empirically disproven); `MulticlassRules` (prereqs + reduced proficiency subsets, all 13
  classes); `MulticlassSpellcasting` (combined caster level, Warlock pact carve-out, per-class ability);
  THREE seeded PHB slot tables (multiclass + half-caster + third-caster) with provenance;
  `CharacterResolutionService` fork (single-class → own class table; ≥2 spellcasting classes → combined)
  + per-class save DC / attack + `check_multiclass`; SEC-08 per-user MCP tools. Final opus review caught
  the single-class half/third-caster slot bug → fixed (Task 12). **Follow-up loose end below (UI).**
- **Item 3 — Auto-NeedsReview grounding cascade** ⬜ ← candidate NEXT (own brainstorm→spec). Tier 1 =
  embedding check (reuse mxbai `dnd_blocks` vectors → promote); Tier 2 = qwen3 judge on residual
  (promote/decline/keep, bias decline-when-unsure). Reuse `errorsOnly` as re-ground pass; can re-audit
  `declined.json` false-drops. Cannot auto-PROVE fine cells — stay needsReview+provenance.
- **Item 4 — Corpus-wide dedup** ⬜ — the DMG Object work produced the dup pattern this targets
  (Ballista/Cannon each twice: `dmg14.monster.*` + `dmg14.item.*`). Dedup by canonical id /
  `EntityNameIndex` key, OUT of the extraction path (separate, reviewable).

## LOOSE ENDS / follow-ups
- **HeroDetail multiclass-editing UI** 🔄 (IN PROGRESS, started 2026-07-05) — the Blazor
  `CompanionUI/Components/Pages/Campaigns/HeroDetail.razor` edit form is still single-class: it binds
  `_editClass`/`_editLevel`/`_editSubclass` locals and `ConfirmSaveAsync` calls `SetSingleClass`, which
  COLLAPSES a genuine multiclass hero to one class on save. Blast radius is zero today (no UI creates a
  multiclass hero), but it must be fixed before multiclass authoring is real. Flagged by the Item 2 final
  review (T1). Now being tackled (brainstorm → build).
- **Qdrant scalar int8 quantization:** shipped (`11c7665`, archived); LIVE validation was done during the
  Item-2 session (config applied, recall preserved, ~4× vector memory). Effectively closed.
- **Spec housekeeping:** `extraction-think-mode` spec (config-toggle form) is proposed, not applied
  (`/no_think` already shipped in `803da7b`).
- **DMG Object residuals** (hand-correctable): tighten `StatBlockScanner` naming / `IsObjectStatBlock`
  (over-scan artifacts).

## How we progress (discipline — never skip)
Each item: **superpowers:brainstorming** (full dialogue) → **opsx:propose** (spec in
`openspec/changes/<name>/`) → **superpowers:writing-plans** → **superpowers:subagent-driven-development**
(per-task TDD + reviewer subagents; final whole-branch review on opus). Work DIRECTLY on main — no
feature branches (`mem:workflow/work_on_main`); commit autonomy granted. FINISH on "commit": commit →
`openspec archive` → run `skill-optimizer` (`mem:workflow/finishing_a_spec`).

## Current position (2026-07-05)
Extraction/retrieval FOUNDATION complete; **Item 2 (multiclass) SHIPPED + archived** — the first
genuinely "reasons not retrieves" feature is done. Immediate work: the HeroDetail multiclass-editing UI
follow-up (in progress). After that, the frontier is **Item 3 (grounding cascade)** or **Item 4 (dedup)**.
Relates to `mem:extraction/dmg_generic_backfill_status`, `mem:project_entity_extraction_rethink`.
