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
- **A few spells — transient LLM empty-responses.** A handful (Command, Gust of Wind; also the monster
  Pseudodragon) hit "Empty response from Ollama" after the existing 3-attempt retry, so they are recorded
  in `errors.json`. These are **not a code bug** — the existing `errorsOnly` retry recovers them; the fix
  is operational (run an `errorsOnly` pass after the main extraction). *(Earlier this was mis-hypothesised
  as a page→category misalignment; that was disproven — Fireball p242 and Wish p289 extracted fine on the
  same page band where Gust of Wind p249 failed, so the TOC mapping is correct.)*

This change fixes the three structural causes (clean headings, prior-type collisions, race fallback) and
relies on the existing `errorsOnly` retry for the transient empties. The harder, lower-yield tail (~17
prose-merged / special-char spell names, and the ~5 missing-`Casting Time` anchors) is **explicitly
deferred to the roadmap**.

## What Changes

- **Clean spell-name headings.** Apply the existing level/school strip to heading-tagged blocks that look
  like a spell name+level/school, so `PRESTIDIGITATIONTransmutation cantrip` → `Prestidigitation`.
- **Prior-type-preferred collision resolution.** When a candidate's name matches a 5etools entry of a
  type *different* from the candidate's primary prior, prefer a same-prior-type match (or defer to
  content-first) rather than forcing the colliding cross-type. Recovers Dwarf (Race not Monster),
  Darkvision/Divination (Spell not invocation/school).
- **Race-section fallback.** Recover a race whose bare title was not tagged by anchoring on its
  `"<RACE> TRAITS"` heading → promote `"<RACE>"` (the race analog of the spell splitter). Recovers Gnome.
- **Recover transient empties operationally.** Run an `errorsOnly` retry pass after the main extraction to
  recover the few entities that hit transient empty Ollama responses (no code change).

## Capabilities

### Modified Capabilities
- `mineru-pdf-conversion`: the spell-chapter splitter also cleans spell-name headings, and a race-section
  fallback recovers races whose title was not tagged.
- `deterministic-type-resolution`: a name match of a type different from the candidate's primary prior no
  longer forces that cross-type; the candidate's prior type is preferred (or it defers to content-first).

## Impact

- Code: `MinerUPdfConverter` (heading cleanup + race fallback), `DeterministicTypeResolver` /
  `EntityNameIndex` (prior-preferred matching). Unit tests per fix. No TOC change.
- Validation: re-extract PHB through the live `mineru:8000` service, then an `errorsOnly` pass; expect
  **9/9 races** (Dwarf, Gnome recovered) and a higher spell count (the cleaned-heading + collision wins +
  recovered transient empties), with **zero new noise** and the same clean declines. No DB/API-contract change.
- Non-goals: prose-merged / special-char spell names and missing-`Casting Time` anchors — deferred to the
  roadmap.
