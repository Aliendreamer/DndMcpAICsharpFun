## Context

The structured-entity extraction layer mis-extracts the *body* of an official book's chapters as
entities. The live PHB re-run (2026-06-28) showed only ~22 of the first 100 candidates were real;
~78 were class features, race stat-block field labels, chapter headings, flavor sidebars, name lists,
and OCR garble. The prior `phb14.json` carried 397 "Class" entities (PHB has 12). The merged
`extraction-name-resolution` change fixed recall (5etools match → canonical name + forced type) but
left this precision hole: a non-matching candidate falls to content-first, which extracts it anyway
because the scanner's chapter bookmark gives it a plausible prior type.

5etools is a COMPLETE cross-source list for the well-covered types (verified: 361 PHB spells, the full
bestiary, every class/race/background/feat/condition, 563 deities incl. 195 PHB). For an **official**
book we can therefore treat 5etools as an authoritative allowlist: a gated-type candidate that does
not match is noise.

This change depends on `extraction-name-resolution` (the `EntityNameIndex`/`EntityNameMatcher` and the
resolver's 5etools step-1) already being on `main` (merged `e80976a`). The MODIFIED delta below is
written as the full end-state ladder; archive `extraction-name-resolution` first, then this change.

## Goals / Non-Goals

**Goals:**
- Stop official books producing chapter-body noise — without losing recall or real entities.
- Make the rejection deterministic (no LLM call) and auditable (a declined-records file).
- Keep homebrew and ungated types on exactly today's content-first behavior.

**Non-Goals:**
- Gating Item, MagicItem, or Plane (Item/MagicItem coverage uncertain; Plane has no source array).
- Building the NeedsReview grounding cascade (roadmap Item 3) — declined records are merely auditable.
- Changing field/content extraction — 5etools still sets name + type only, not fields.

## Decisions

**1. Gate the 8 well-covered types only.** Spell, Monster, Class, Race, Background, Feat, Condition,
God. These are the types where the local mirror is provably complete, so a non-match is reliably noise.
Item/MagicItem/Plane stay on content-first. *(Alternative: gate all matchable types — rejected: higher
false-drop risk where coverage is uncertain.)*

**2. The gate lives in `DeterministicTypeResolver` as a new `Decline` outcome.** The resolver already
runs the matcher as step 1 and owns the type ladder; adding the gate there keeps the type decision in
one place. It needs two new inputs: an `isOfficial` flag and the gated-type set. *(Alternative: a
separate orchestrator filter stage — rejected: splits the type decision across two components.)*

**3. The stat-block rescue guard always wins, even for official books.** A non-matching candidate with
a complete stat block is force-typed Monster regardless of `isOfficial`, so a real monster missing
from the mirror is never dropped. The (small) cost is that stat-block-shaped official noise survives —
acceptable, since chapter-body noise rarely carries a full stat block. *(Alternative: gate overrides
the guard for official books — rejected by design review to never risk dropping a real monster.)*

**4. Decline only when ALL of the candidate's prior types are gated.** A mixed prior that includes an
ungated type (e.g. `[Class, Item]`) falls through to content-first, so an ungated possibility is never
foreclosed. Empty prior → content-first (cannot determine). This keeps the gate conservative.

**5. Non-match → decline with no LLM call, recorded to `<book-slug>.declined.json`.** A separate
sibling file (`{id, name, type, reason: "no_5etools_match"}`), NOT the main `entities` and NOT
`errors.json` — so `errorsOnly` retry ignores deliberate declines. Auditable: grep the file to catch
any real entity wrongly declined.

**6. "Official" = the book record has a non-empty `FivetoolsSourceKey`.** Per CLAUDE.md every WotC
book is registered with its source key; homebrew omits it. No new field needed.

## Risks / Trade-offs

- **A real official entity missing from the local mirror, or with an OCR-garbled name below the 0.90
  fuzzy threshold, is wrongly declined** → mitigated by the auditable `declined.json` (post-run review)
  and the mirror being verified-complete for the 2014 core books. The future NeedsReview cascade can
  re-examine declined records.
- **Stat-block-shaped official noise survives** (Decision 3) → accepted; rare in practice, and far
  smaller than the chapter-body noise this removes.
- **Two unarchived changes modify `deterministic-type-resolution`** (`extraction-name-resolution` and
  this) → archive `extraction-name-resolution` first; this delta is written as the full end-state.
- **Mixed/empty priors slip through to content-first** (Decision 4) → may leave a little residual
  noise, but never wrongly declines — the conservative trade.

## Migration Plan

No DB schema or API contract change. Deploy = rebuild the app image. Validation = the live PHB
re-run (force) on the combined pipeline, expecting Class 397→~12, race fields gone, `declined.json`
populated, recall + real entities intact. Rollback = revert the change; `declined.json` is additive
and ignored by ingestion.

## Open Questions

None — the design decisions above were resolved during brainstorming.
