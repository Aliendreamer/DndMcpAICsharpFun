## Context

Phase 1 (`openspec/changes/archive/2026-11-05-programmatic-extraction-phase1/`) built the Python book extractor
(`Tools/BookExtractor`). It does RapidOCR on CPU with a per-page cache, and rebuilds reading order. Pluggable parsers
are registered in `assemble._PARSERS`. Common checks, provenance and the report sit on top, and the C# runner applies
overrides and ingests. All 16 registered books now run through it live, but it only produces Monster, Spell and
MagicItem. The umbrella design is `phase2-umbrella-design.md` in this change. It splits
Phase 2 into 2a (character data, this change), 2b (equipment and hazards) and 2c (world).

The consumers already exist. `Domain/Entities/Fields/{Class,Subclass,Race,Subrace,Feat,Background}Fields.cs` are
5etools-shaped. The coach (`Features/CharacterAdvice/*`, `LevelUpPlanner`, `ClassFeatureRefParser`) reads
`classFeatures` and `subclassFeatures` as 5etools ref strings (`Name|Class|Source|Level` and the 6-part subclass form),
plus `hd.faces`, `subclassTitle`, `multiclassing` and spellcasting ability.

Owner decisions that bind this change:

- **D4:** all checks pass → Accepted, any failure → NeedsReview.
- **D9:** no staging.
- Never guess names.
- No LLM, and no 5etools data at runtime.
- The review load is a one-time cost.
- Done bar: zero unexplained wrong-and-accepted, and at least 90% of each book's 5etools entries for the six types
  found.

## Goals / Non-Goals

**Goals:**

- Class, Subclass, Race, Subrace, Feat and Background from all 16 books, in the existing field shapes, with per-type
  self-checks.
- A class level-table reader good enough for the 12 PHB tables and the subclass feature tables in XGE and TCE.
- Scorer truth and the per-book score table for the six types, and the wrong-and-accepted audit extended to them.
- The coach works off the new data with no coach code change.

**Non-Goals:**

- Equipment, hazards and world types (2b, 2c).
- Optional class features (TCE "Optional Class Features"), Eldritch Invocations, Fighting Styles and other
  `optionalfeatures`, which 2c picks up as Rule unless the coach needs them sooner.
- SRD and Basic Rules. The 2024 books (XPHB).
- Any coach behaviour change.

## Decisions

1. **Anchors, then names from headings.** Each type is found by a printed anchor, and its name comes only from the
   nearest qualifying heading above it, the same rule as Phase 1's `name-found`.

   | Type | Anchor | Name source |
   | --- | --- | --- |
   | Class | the "Hit Dice:" line under a "Class Features" heading | the chapter-level class heading |
   | Subclass | the class's subclass-group heading (the plural of `subclassTitle`, e.g. "Primal Paths", "Arcane Traditions") | each heading below it whose text has "At Nth level" feature cues; in XGE and TCE, also the "<Subclass> Features" table |
   | Race | an "Ability Score Increase." trait line with "Size." or "Speed." traits nearby | the "<Race> Traits" heading, minus "Traits" |
   | Subrace | a heading inside a race section, after the race's own traits, followed by its own "Ability Score Increase." | that heading |
   | Feat | headings inside a section titled "Feats" (or a chapter whose section tree is feats) | the heading; an optional "Prerequisite:" line follows |
   | Background | a "Skill Proficiencies:" line | the heading above it |

   *Alternative considered:* a book-specific page map. Rejected: there are 16 books and they get re-run.

2. **The table reader clusters OCR boxes.** Rows come from y-overlap and columns from header x-spans. Cells are
   assigned by x-overlap with the header spans. Digit repair applies per cell. It is a module of its own
   (`tables.py`) so 2b (weapons, armor, gear) reuses it. *Alternative:* the PDF text layer. Rejected because Phase 1
   measured it as unreliable on these scans.

3. **Features as synthesized refs.** `classFeatures` is built from the level table's Features column, plus feature
   headings found in the text, as `Name|Class|<SOURCE>|Level`. The subclass refs use the 6-part form. `SOURCE` is the
   5etools key of the defining book. A subclass's `classSource` is the key of the book that defines the parent class
   (an XGE subclass of a PHB class gets `PHB`). Feature text goes into `entries`. There is no new ClassFeature entity
   type.

4. **Fields the book does not print stay null.** A missing optional field is not a failure. A printed field that
   cannot be parsed fails its check.

5. **A table that fails its checks ships as NeedsReview with partial data.** That lets each parser ship before the
   table reader is perfect.

6. **Reprints are separate entities per book**, with ids `<slug>.<type>.<name-slug>`. Subraces use
   `<slug>.subrace.<race>-<subrace>` so "High" doesn't collide across races.

7. **Fixture-first development.** Each parser starts from committed OCR fixture pages (`tests/fixtures/ocr/<book>/`)
   with tests written against them, then whole-book scoring. Each fix round ends with the all-book score diff plus
   the wrong-and-accepted audit, as in Phase 1, so Phase 1 scores can't regress unnoticed.

## Risks / Trade-offs

- **Class tables are wide (up to 15 columns) and OCR splits or merges cells.** Mitigation: the proficiency-bonus and
  level columns are fixed patterns that anchor rows, a failed table is NeedsReview, and the done bar is measured.
- **Subclass detection in supplements varies.** XGE and TCE group subclasses under class headings; SCAG and EGW embed
  them in prose chapters. Mitigation: the subclass-group heading is the anchor, plus the scorer's per-book recall
  numbers.
- **Feats in books other than PHB, XGE and TCE sit in racial or setting chapters.** Mitigation: the section-tree rule
  plus "Prerequisite:" lines, and recall is measured per book.
- **Heading OCR casing noise** ("REcKLEsS ATTAcK"). Mitigation: Phase 1's vocabulary-based `repair_name`, extended
  to feature headings.
- **Coach regressions if refs are malformed.** Mitigation: a C# test parses every ref in the new PHB file with
  `ClassFeatureRefParser`. After the live run, a level-up smoke through the Ollama probe technique checks the
  advice.

## Migration Plan

Development is local, against the OCR cache copy, scoring each book. When the done bar is met: rebuild the
`book-extractor` and `app` containers, queue `extract-programmatic` for all 16 books (CPU only, a few minutes), check
the live files are identical to the local runs, commit the canonical files, then run the finish routine. Rollback is
`git revert` of the canonical commit followed by `ingest-entities`.

## Open Questions

None blocking. Any per-book layout surprise is ruled on during fix rounds and recorded in the ledger.
