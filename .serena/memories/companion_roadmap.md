# D&D Companion — Roadmap & Progress (living doc; update markers as we go)

**North star:** a D&D companion agent that REASONS (character build / encounter design / setting-aware lore), not just retrieves. RAG + extraction are the *means*. Foundation (honest extraction) + the first reasoning slice are SHIPPED.

## Status legend: ✅ done · 🔄 in progress · ⬜ not started

## Track 1 — WRITE-layer close-out  (~90% done)
- ✅ Archived `consolidate-extraction-signatures` (deterministic-type-resolution spec synced)
- ✅ MM resolver re-run (230 entities; **residuals flagged**: Aboleth MIA, 12 lair-misfires, OCR-garbled names)
- 🔄 PHB resolver re-run (in its container; writes `phb14.json` via the slug fix)
- ⬜ Delete `books/canonical/playerhandbook-2014.json` orphan once PHB writes `phb14.json` (auto via scheduled wakeup, on MAIN)
- DMG already on the resolver pipeline (551 entities, validated).

## Item 2 — Slice 2: multiclass spellcaster  ⬜ (next big build, RECOMMENDED)
The design's deliberate stress test — "if it survives multiclass spellcasting, it survives D&D." Extends slice-1 rails (`CharacterResolutionService`, structured-fact store, `resolve_character_feature` MCP tool).
- `CharacterSheet.Level` (single int) → `List<ClassLevel>{class,level,subclass}`, total derived (the slice-1 sheet change deliberately deferred this).
- Combined-caster-level slot math (full + ½ half + ⅓ third casters; Warlock Pact Magic separate) → one Multiclass Spellcaster table.
- Bin-C queries FORK: single-class vs multiclass resolution paths (the engine tool surface branches).
- Design home: `openspec/changes/prose-grounded-knowledge-model/design.md` §J.

## Item 3 — Auto-NeedsReview grounding cascade  ⬜ (own brainstorm→spec)
Automatically resolve NeedsReview entities. Tiers (design'd, not built): Tier 1 = embedding check (reuse mxbai dnd_blocks vectors, ~0.3s → promote to Accepted); Tier 2 = qwen3 judge on the residual (promote/decline/keep). Bias decline-when-unsure (safety property). Delivery: reuse the `errorsOnly` re-run pattern as a "re-ground" pass. Also: warnings (inter-book dangling refs) are fully auto-fixable via a corpus-wide re-resolution pass after all books ingested. Limit: cannot auto-PROVE fine factual cells (Red→fire) — those stay needsReview+provenance for human spot-check.

## Item 4 — WRITE-layer follow-ups  ⬜ (cheap)
- **Aboleth MIA** investigation: it vanished from the MM resolver run (not in canonical, not in errors, not declined) — likely a Marker candidate-generation/naming issue.
- **Lair-name filter**: extend `ExtractionSignatures.IsEntityLikeName` to reject "A X's LAIR" headings (12 lair sections typed Monster in MM) — same class as the merged Creating-*/*-Features fix.

## How we progress (the discipline — never skip)
Every item: **superpowers:brainstorming** (full dialogue, one-at-a-time) → **opsx:propose** (spec in `openspec/changes/<name>/`) → **superpowers:writing-plans** (plan saved to `openspec/changes/<name>/plan.md`) → **superpowers:subagent-driven-development** (feature branch, per-task TDD + reviewer, final whole-branch review on opus) → present for merge (user approves; nothing lands on main without it) → `openspec archive`. Update THIS memory's ✅/🔄/⬜ markers as items move. See [[feedback_skill_workflow]], [[feedback_no_autocommit]].

## Current position (2026-06-27)
Slice 1 (`character-fact-resolution`) MERGED to main (`235699a`, 746 tests green) — companion computes & cites a Red Dragonborn's breath weapon end-to-end. Track 1 nearly closed. **Next decision:** start Item 2 (slice 2, recommended) vs Item 3 vs Item 4. Relates to [[extraction_pipeline_state]].
