## Why

Hybrid path to 361, A-iteration 2. After iteration 1 (single-block split, +15 → 350/361), several of the
11 remaining spells have a clean bare header block — `"GREATER RESTORATION 5th-level abjuration"`,
`"SHIELD OF FAITH 1st-level abjuration"` — but the `Casting Time:` line was OCR-dropped (the block is
followed directly by `Components:`). No existing splitter fires: the separate-block splitter needs a bare
level/school line + a following `Casting Time:` block; the single-block splitter needs `Casting Time`
inside the block. So these never become candidates.

## What Changes

- **Promote a bare spell-header block without a Casting-Time anchor** (`MinerUPdfConverter`): when a `text`
  block's first line is a short bare spell header — `NAME <Nth-level> <school>` or `NAME <school> cantrip`
  (i.e. `IsLevelSchoolLine(firstLine)` is true, `StripLevelSchool(firstLine)` yields a non-empty name, and
  the line is short, header-shaped) — promote the name to a synthetic `section_header`, even with no
  `Casting Time` nearby. Recovers Greater Restoration, Shield of Faith, and similar OCR-dropped-anchor spells.

## Capabilities

### Modified Capabilities
- `mineru-pdf-conversion`: the converter promotes a bare spell-header text block (name + level/school) to a
  heading even when the Casting Time line is missing/OCR-dropped.

## Impact

- Code: `MinerUPdfConverter` (bare-header detection), reusing `IsLevelSchoolLine`/`StripLevelSchool`. Unit
  tests. Tight guard (short first line + non-empty name) to avoid promoting spell-LIST rows (which start
  with the level, e.g. `"5TH LEVEL Banishing Smite ..."` → StripLevelSchool empty → skip) or prose.
- Validation: clear cache, re-extract PHB + errorsOnly + re-add Gnome; expect Greater Restoration + Shield
  of Faith (+ any similar) recovered, spell count > 350, no new noise/junk, counts otherwise unchanged.
  Record which of the 11 remain → iteration 3 or B.
- Non-goals: list-only entries whose description block is itself broken (may need iteration 3 or B);
  Regenerate (not co-located).
