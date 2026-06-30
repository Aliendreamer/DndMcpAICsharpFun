## Why

The `extraction-recall-fixes` gap investigation found 6 PHB spells that vanish despite having a valid
heading + body — and the cause is not one bug but two crisp structural ones (the rest stay deferred):

- **Bug A — splitter over-cuts spell names containing a school word.** `StripLevelSchool` cuts a promoted
  spell name at the *first* school word, but the name can legitimately contain one. `MINOR ILLUSION
  Illusion cantrip` → cut at the first "Illusion" → `MINOR`; the cache literally holds `section_header
  "MINOR"`/`"PROGRAMMED"` and `declined.json` holds `MINOR`, `PROGRAMMED` — wrong names, no 5etools match.
  Loses **Minor Illusion, Programmed Illusion** (and any spell whose name ends in a school word).
- **Bug B — scanner merges same-titled sections across the whole book.** `EntityCandidateScanner.Scan`
  does `GroupBy(SectionTitle)` and keys each group on `Min(Page)`. A name reused in two chapters fuses
  into one candidate keyed at the earlier page → wrong chapter → mis-typed or skipped. `Darkvision` (the
  spell, p230) merges with the `Darkvision` Eldritch Invocation heading (p184) and is lost. Affects any
  name that recurs corpus-wide.

## What Changes

- **Bug A fix — strip the level/school *suffix*, not the first school word.** `StripLevelSchool` removes a
  trailing `"<Nth-level> <school>"` or `"<school> cantrip"` tail (anchored at the end), keeping the
  digit-cut for leveled spells (unambiguous — names carry no digits). `MINOR ILLUSION Illusion cantrip`
  → `MINOR ILLUSION`.
- **Bug B fix — page-proximity merge guard.** The scanner merges same-titled blocks only when they are
  contiguous / within a small page window; same-titled sections far apart become **separate candidates**,
  each keyed on its own page. The legitimate continuation-page merge (a section whose header repeats on
  the next page) is preserved; the cross-chapter merge is not.

## Capabilities

### Modified Capabilities
- `mineru-pdf-conversion`: the splitter strips the level/school suffix (not the first school word), so a
  spell name that contains a school word survives.
- `entity-extraction-pipeline`: the candidate scanner groups same-titled sections only within page
  proximity, so a name reused across chapters yields one candidate per occurrence.

## Impact

- Code: `MinerUPdfConverter.StripLevelSchool` (suffix strip) + its regexes; `EntityCandidateScanner.Scan`
  (proximity-guarded grouping). Unit tests per bug.
- Validation: re-extract PHB through the live service (clear the `*.mineru.json` cache first; early
  checkpoint spot-check). Expect **Minor Illusion, Programmed Illusion, Darkvision** recovered, spell
  count up from 329, classes/races/Monster unchanged, no new noise.
- Non-goals: the residual bucket **C** (Shield of Faith's missing Casting-Time anchor; Mordenkainen's ×2
  and Gnome's still-unpinned single-header vanish) — a separate follow-up investigation. The 21
  prose-merged / 5 OCR-anchor spells stay on the roadmap.
