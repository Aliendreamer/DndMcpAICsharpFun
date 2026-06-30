## Context

Two isolated, well-understood bugs surfaced by the `extraction-recall-fixes` gap dive, in two different
stages: Bug A in the converter's spell splitter (`MinerUPdfConverter`), Bug B in `EntityCandidateScanner`.
Both are structural (not data-specific) and fixable with small, testable changes. The downstream resolver
+ 5etools gate are unchanged.

## Goals / Non-Goals

**Goals:** recover Minor Illusion, Programmed Illusion (Bug A) and Darkvision (Bug B); stop both classes
of bug corpus-wide (any school-word-name spell; any cross-chapter name reuse). No regression to the
existing 9/9 races, 12/12 classes, 30 monsters, 329 spells.

**Non-Goals:** bucket C (Shield of Faith's missing Casting-Time anchor; the still-unpinned single-header
vanish of Mordenkainen's ×2 and Gnome) — a separate follow-up. The 21 prose-merged / 5 OCR-anchor spells
stay on the roadmap.

## Decisions

**Bug A — strip the level/school SUFFIX (`StripLevelSchool`).** The promoted block is `"<NAME> <suffix>"`
where the suffix is exactly one of `"<Nth-level> <school>"` or `"<school> cantrip"`. Replace "cut at the
first digit/school/OCR-level token" with:
1. If a level digit is present → cut at the first digit (unchanged; names have no digits — handles
   `SHIELD OF FAITH 1st-level abjuration` → `SHIELD OF FAITH`).
2. Else (cantrip) → strip a **trailing** `"<school> cantrip"` (the school immediately before `cantrip`),
   not the first school word: `MINOR ILLUSION Illusion cantrip` → `MINOR ILLUSION`;
   `PRESTIDIGITATION Transmutation cantrip` → `PRESTIDIGITATION`.
The school/cantrip regexes (`SchoolRx`/`CantripRx`/`OcrLevelWordRx`) stay; only the cut-point selection
changes (anchor on the end, not the first match). *(Alt A2 "cut at the last school word" rejected — fails
if a name repeats a school word; A1 targets the real suffix grammar.)*

**Bug B — page-proximity merge guard (`EntityCandidateScanner.Scan`).** Today
`.GroupBy(x => x.Block.SectionTitle)` merges every block sharing a title and keys on `Min(Page)`. Replace
with a single ordered pass that starts a **new group** whenever the `SectionTitle` changes OR the block's
page jumps more than `W` pages beyond the current group's page span (default `W = 3`). Same-titled blocks
far apart (Darkvision invocation p184 vs spell p230, gap 46) → two groups, each keyed on its own page →
each typed by its own chapter; a header repeated on the *next* page still merges. Each group keeps the
existing shape (Section, FirstIndex, min Page, joined Text). *(Alt B3 "merge within same TOC category"
rejected — couples grouping to the page→category lookup; proximity is simpler and chapter boundaries are
already page boundaries.)*

## Risks / Trade-offs

- **A over- or under-stripping** a cantrip whose name legitimately ends in a school word followed by a
  non-`cantrip` token → mitigated: only strip a `<school>` that is immediately followed by `cantrip` (or
  preceded by a level digit); a bare trailing school word is left alone. Validate the spell count holds/rises.
- **B splitting a genuine continuation section** whose header repeats >W pages apart → unlikely (MinerU
  tags a heading once per section); `W = 3` covers normal continuation. If a real entity splits, the
  id-keyed `ExtractionCandidateDeduplicator` already collapses same-id candidates.
- **B changing existing candidate boundaries** → the live PHB re-run is the guard: classes/races/monsters
  counts must hold; only the cross-chapter-merge victims should change.

## Validation

Unit tests per bug (StripLevelSchool cases; scanner same-title-far-apart vs adjacent). Then the live gate:
**clear `books/conversion-cache/*.mineru.json`** (Bug A is in the converter), re-extract PHB through
`mineru:8000`, early-checkpoint spot-check, confirm Minor Illusion / Programmed Illusion / Darkvision
present, spell count > 329, and classes 12 / races 9 / Monster 30 unchanged with no new noise.
