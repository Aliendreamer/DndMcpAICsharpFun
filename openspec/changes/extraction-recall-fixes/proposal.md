## Why

The `mineru-main-parser` live run (PHB, 2026-06-29) shipped a strong result but left a measured tail:
**2 races and 38 spells** are still cut. A deep-dive root-caused them — they are not one bug but
several distinct, mostly-tractable causes:

- **Dwarf — mis-typed, not missing.** Its candidate matched a 5etools *"Dwarf" creature* and resolved
  to **Monster** instead of Race, despite sitting in the races chapter. (Same family: spells Darkvision,
  Divination collide with an invocation / a school heading.)
- **Gnome — heading miss.** MinerU tagged `GNOME NAMES`/`GNOME TRAITS`/`Rock Gnome` but not the bare
  `GNOME` title, so no clean Gnome race candidate exists.
- **~6 spells — uncleaned spell *headings*.** When MinerU tags a spell name *with* the level/school
  glued on (`PRESTIDIGITATIONTransmutation cantrip`, `GUIDING BOLT Ist-level evocation`), the splitter's
  name-cleanup — which only runs on non-heading blocks — is skipped, so the dirty name fails to match.
- **~8 spells — page→category misalignment.** The candidate is promoted, but `EntityCandidateScanner`
  drops it because `TocCategoryMap.GetCategory(page)` returns the wrong/Unknown category for that page
  (`continue`), so the candidate never reaches extraction (e.g. Gust of Wind).

This change fixes those four causes. The harder, lower-yield tail (~17 prose-merged / special-char spell
names, and the ~5 missing-`Casting Time` anchors) is **explicitly deferred to the roadmap**.

## What Changes

- **Clean spell-name headings.** Apply the existing level/school strip to heading-tagged blocks that look
  like a spell name+level/school, so `PRESTIDIGITATIONTransmutation cantrip` → `Prestidigitation`.
- **Prior-type-preferred collision resolution.** When a candidate's name matches a 5etools entry of a
  type *different* from the candidate's primary prior, prefer a same-prior-type match (or defer to
  content-first) rather than forcing the colliding cross-type. Recovers Dwarf (Race not Monster),
  Darkvision/Divination (Spell not invocation/school).
- **Race-section fallback.** Recover a race whose bare title was not tagged by anchoring on its
  `"<RACE> TRAITS"` heading → promote `"<RACE>"` (the race analog of the spell splitter). Recovers Gnome.
- **Fix page→category misalignment.** Diagnose and correct why in-chapter spell pages resolve to a
  non-Spell/Unknown category, so promoted spell candidates are not silently dropped by the scanner.

## Capabilities

### Modified Capabilities
- `mineru-pdf-conversion`: the spell-chapter splitter also cleans spell-name headings, and a race-section
  fallback recovers races whose title was not tagged.
- `deterministic-type-resolution`: a name match of a type different from the candidate's primary prior no
  longer forces that cross-type; the candidate's prior type is preferred (or it defers to content-first).
- `heading-derived-toc-fallback`: in-chapter pages resolve to their chapter's category so promoted
  candidates on those pages are not dropped by `EntityCandidateScanner`.

## Impact

- Code: `MinerUPdfConverter` (heading cleanup + race fallback), `DeterministicTypeResolver` /
  `EntityNameIndex` (prior-preferred matching), the TOC page-category mapping. Unit tests per fix.
- Validation: re-extract PHB through the live `mineru:8000` service; expect **9/9 races** (Dwarf, Gnome
  recovered) and **~340+/361 spells** (the #1 + #3 + #5 wins), with **zero new noise** and the same clean
  declines. No DB/API-contract change.
- Non-goals: prose-merged / special-char spell names (#4) and missing-`Casting Time` anchors (#2) —
  deferred to the roadmap.
