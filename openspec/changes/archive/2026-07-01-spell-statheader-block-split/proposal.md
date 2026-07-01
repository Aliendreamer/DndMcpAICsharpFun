## Why

Hybrid path to 361 spells, A-iteration 1. Of the 26 missing PHB spells, 21 have their entire spell stat
header collapsed into ONE multi-line `text` block (name on line 1), e.g.:

```
CLOUD OF DAGGERS
2nd-level conjuration
Casting Time: 1 action
Range: 60 feet ...
```

The existing splitter anchors on a level/school line and a `Casting Time:` block that are SEPARATE
blocks; when they are glued into one block, no anchor fires and the spell never becomes a candidate.

## What Changes

- **Add a single-block stat-header splitter** (`MinerUPdfConverter`): when a `text` block contains a
  level/school token AND `Casting Time` within it, treat it as a spell stat header — promote the spell
  NAME (the text before the first level/school marker, on the first line) to a synthetic `section_header`,
  so the block's own stat text + the following description become the spell's body. Handles both the
  newline-separated form (`NAME\n2nd-level ...`) and the space-glued form (`DISGUISE SELF 1st-level ...`).

## Capabilities

### Modified Capabilities
- `mineru-pdf-conversion`: the converter recovers a spell whose entire stat header (name + level/school +
  Casting Time) is a single multi-line block, by promoting the name to a heading.

## Impact

- Code: `MinerUPdfConverter` (single-block stat-header detection + name promotion), reusing the existing
  `StripLevelSchool`/`SchoolRx`/`LevelRx`/`CantripRx`. Unit tests.
- Validation: clear cache, re-extract PHB + errorsOnly, re-add Gnome; expect most of the 21 recovered
  (Cloud of Daggers, Blindness/Deafness, Divine Favor, Disguise Self, …), spell count up from 335, no new
  noise/junk, classes/races/Monster unchanged. Record which of the 26 remain (→ A-iteration 2 or B).
- Non-goals: the not-co-located entries (Regenerate) + missing-anchor (Feeblemind, Tree Stride) + Shield
  of Faith — later iterations / B backfill.
