## Context

A-iteration 1 of the hybrid 361-spell push. Isolated converter change; the splitter already handles the
separate-blocks case, this adds the single-block case. Downstream unchanged.

## Decisions

**Single-block stat-header splitter (`MinerUPdfConverter`).** For a `text` block (`TextLevel is null or 0`)
whose text contains BOTH a level/school token (`LevelRx`/`SchoolRx`/`CantripRx`) AND `Casting Time`
(case-insensitive): take the FIRST LINE (up to the first `\n`, or the whole text if single-line), extract
the name via `StripLevelSchool` (text before the first digit/school/cantrip marker). If the name is
2–40 chars and non-empty and != lastHeadingNorm, emit a synthetic `section_header` for it BEFORE emitting
the block's text. The block's stat text is still emitted as `text` (body), and the following description
block inherits the section. Guard: require the level/school token to appear in the FIRST ~60 chars (so a
prose block that merely mentions "casting time" somewhere isn't split), and require the name prefix to be
non-empty and mostly uppercase-word-like (a real spell name), to avoid false promotions.

*(Alt: split the block into name + stat text as two items — rejected: emitting the name as a heading +
keeping the stat text as body is enough for the scanner to attribute the section; no need to rewrite the
block.)*

## Risks / Trade-offs

- **False promotion** of a prose block containing "casting time" mid-text → mitigated by the first-~60-char
  level/school requirement + the name-shape guard. Validate no new noise/junk entities.
- **Double promotion** (this block + a separate-block anchor for the same spell) → the scanner's id-keyed
  dedup + `lastHeadingNorm` collapse duplicates; verify spell count rises without dup ids.

## Validation

Unit tests: a `CLOUD OF DAGGERS\n2nd-level conjuration\nCasting Time:...` block → emits section_header
"CLOUD OF DAGGERS"; a space-glued `DISGUISE SELF 1st-level illusion Casting Time:...` → "DISGUISE SELF"; a
prose block mentioning "casting time" with no leading name/level → NOT split. Then live: clear cache,
re-extract PHB + errorsOnly + re-add Gnome; confirm ≥15 of the 21 recovered, spell count > 335, no
noise/junk, counts otherwise unchanged.
