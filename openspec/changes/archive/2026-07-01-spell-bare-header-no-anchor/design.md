## Context

A-iteration 2 of the hybrid 361 push. Isolated converter change; drops the Casting-Time requirement for a
clearly-shaped bare spell header. Downstream unchanged.

## Decisions

**Bare-header promotion (`MinerUPdfConverter`).** For a `text` block (`TextLevel is null or 0`): take the
first line (up to first `\n`, else whole text). If `IsLevelSchoolLine(firstLine)` is true AND
`StripLevelSchool(firstLine)` returns a 2–40-char non-empty name AND the first line is short (≤ ~55 chars,
so it is a header not a prose sentence that merely contains a school word) AND the name's norm !=
lastHeadingNorm, emit a synthetic `section_header` for the name. This runs in addition to the existing
separate-block + single-block (Casting-Time-anchored) paths; those already handle their cases, this adds
the no-anchor bare-header case. Order it AFTER the Casting-Time-anchored checks so an anchored header isn't
double-promoted (dedup by lastHeadingNorm also protects this).

*(Alt: require the NEXT block to start with a spell-stat label like `Components:`/`Range:` as a soft anchor
— considered; rejected for now as the short bare-header shape is already specific. Revisit in iteration 3
if false positives appear.)*

## Risks / Trade-offs

- **False promotion** of a short non-spell block shaped like `NAME <level> <word>` → mitigated by the
  IsLevelSchoolLine + non-empty StripLevelSchool + short-line guard; a spell-list row starts with the level
  (StripLevelSchool → empty → skip). Validate no new noise/junk Spell entities.
- **Over-promotion** of a class feature line mentioning a level → the first-line-short + level/school-token
  shape is spell-specific; watch the live noise count.

## Validation

Unit tests: `"GREATER RESTORATION 5th-level abjuration"` (whole short block) → section_header
"GREATER RESTORATION"; `"SHIELD OF FAITH 1st-level abjuration"` → "SHIELD OF FAITH"; a spell-list row
`"5TH LEVEL Banishing Smite Circle of Power ..."` → NOT promoted (name empty); a long prose block
containing a school word → NOT promoted (first line not short/header-shaped). Live: clear cache, re-extract
+ errorsOnly + re-add Gnome; confirm Greater Restoration + Shield of Faith recovered, spell count > 350, no
new noise, counts otherwise unchanged. Record remaining → iteration 3 or B.
