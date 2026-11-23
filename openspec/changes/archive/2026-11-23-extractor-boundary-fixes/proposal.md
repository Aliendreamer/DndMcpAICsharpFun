# Proposal

## Why

The 2026-11-22 page-image review (commit 2851c5f0) cleared the flagged pile with ~400 hand overrides, but it showed the
programmatic extractor still cuts printed text into the wrong entities in six repeatable ways — and two damaged monsters
(Adult Bronze Dragon, Werewolf) had even passed every check. Fixing those boundaries in the extractor stops the same damage
from coming back on every re-run, and the review just produced a page-verified answer key to measure the fixes against.
Without an override change, though, a better extraction would push every reviewed entity back to review as drift.

## What Changes

- Six deterministic boundary fixes in the Python extractor (`Tools/BookExtractor`), each covered by tests built from the
  review's real cases:
  1. A table that continues past a column or page break (its repeated header OCR'd as "Oolp Effect" = `d100 Effect`) is
     joined to its parent table instead of becoming its own entity.
  2. A numbered table row is never read as a heading (GGR p69 "5 I helped a minor Gruul chieftain acquire an Izzet
     weapon" stopped being a MagicItem), so the sections after it are not swallowed.
  3. A stat block ends at its last action / reaction / legendary entry; text after it (ERLW Homunculus Servant's three
     pages, Sacred Statue's eladrin prose) is not absorbed, and traits/actions are never parsed into `languages`.
  4. Both columns of a two-column stat block are read (Adult Gold/Bronze Dragon, Jackalwere lost their right column).
  5. Sidebars and quote boxes are not glued into an entity (Werewolf's all-caps quote, Concentration's "Schools of Magic").
  6. Sibling tables under one heading become separate entities (XGE random-encounter tables, TCE Monsters' Desires).
- Content is never dropped by a fix: text that a boundary fix detaches becomes its own Lore or Rule entity (GGR Izzet
  "How Do I Fit In?", "An Izzet Party", "Rank and Renown"; ERLW infusions and Group Patrons).
- A regression set built from the review: every entity reviewed against its page image (overrides and approvals in
  `books/canonical/*.overrides.json`) with its OCR'd pages; extractor changes are scored against it.
- **BREAKING (overrides behaviour):** an override whose field is re-extracted to exactly the override's value retires
  quietly — no `override-drift`. Drift is raised only when the new extraction differs from both the recorded baseline
  and the override value.
- An override or approval whose entity id disappears after a re-run is carried to the renamed/split entity on the same
  page when the match is certain; otherwise it stays an orphan, listed in the run result.
- Re-extract the affected books and report the regression results.

## Capabilities

### New Capabilities

- `extraction-regression-set`: the page-verified regression fixture built from review overrides/approvals, the scoring
  of extractor output against it, and the gate it puts on extractor changes.

### Modified Capabilities

- `entity-verification-review`: the "Overrides file" requirement changes — retire-on-match instead of drift when the new
  extraction equals the override value, and certain-match migration of orphaned overrides/approvals.
- `programmatic-book-extraction`: "Monster parsing" gains stat-block end and two-column requirements; new requirements for
  table continuations, table rows vs headings, sidebars/quotes, sibling tables, and no-content-dropped boundaries.

## Impact

- Python: `Tools/BookExtractor/bookextractor/` — `layout.py` (column order), `boundary.py` (sidebars, siblings),
  `parsers/sections.py` / `parsers/world.py` / `parsers/rule.py` (headings, tables, continuations),
  `parsers/monster.py` (stat-block end, columns), `assemble.py` (new Lore/Rule entities); new regression fixture +
  builder under `tests/`.
- C#: `Features/Ingestion/Programmatic/OverrideMerger.cs` (retire-on-match, orphan migration) and its tests; the
  extraction run / apply-overrides results report migrated and orphaned overrides.
- Data: re-extracted `books/canonical/<slug>.json` for affected books (at least tce, ggr, erlw, mm14, mpmm, mtf, xge,
  phb14); overrides files rewritten where overrides retire or migrate.
- No API contract change (existing endpoints only); no new dependencies.
