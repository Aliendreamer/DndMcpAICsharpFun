## Why

About 1,110 programmatic entities are still flagged NeedsReview, too many for the owner to clear by hand. A page-image check of 258 flagged sections (ChatGPT) showed that our OCR already holds essentially every correct word; the dominant error is a **section boundary** that runs too far and swallows a neighbouring section, table, shaded sidebar or map label. A throwaway spike showed that three layout rules recover correct text for about half of those trim cases, with little damage to correct sections. Fixing the boundary logic shrinks the review pile now and keeps new books cleaner.

## What Changes

- Add three boundary rules to `Tools/BookExtractor`:
  - **Heading cut**: a section ends at the next heading at least as big as its own heading (anchored on the entity's own heading line, guarded against matching a heading on a continuation page).
  - **Shaded sidebar drop**: a shaded block that opens with its own heading is not part of the section (read-aloud boxes are excluded).
  - **Map-label drop**: lines like `MAP 3`, `1 square = 5 feet`, `Side View` are dropped.
- Add a one-off **trim pass** dev tool (sibling of `fivetools_fix`) that runs the rules over still-flagged entities of already-extracted books and writes the result as overrides in `books/canonical/<book>.overrides.json`. Nothing is re-extracted; approved entities are never touched.
- **Auto-approve** a trim that keeps most of the section; a cut keeping under half the tokens is not written. (Outcome: the one-off run was cancelled — see design.md "Outcome".)
- The same rules run inside extraction for books extracted from now on.

## Capabilities

### New Capabilities

- `section-boundary-trim`: layout rules that end a programmatic section at its true boundary, the one-off trim pass over flagged entities, and its auto-approve gate.

### Modified Capabilities

(none — extraction-time behaviour is added as new requirements under `section-boundary-trim`)

## Impact

- Code: `Tools/BookExtractor/bookextractor/parsers/sections.py`, `parsers/world.py`, `parsers/rule.py`, a new `bookextractor/boundary_fix.py` dev tool, tests under `Tools/BookExtractor/tests/`.
- Data: `books/canonical/<book>.overrides.json` and the re-applied `<book>.json` for the 16 extracted books (not the SRD); applied through the existing `POST /admin/books/{id}/apply-overrides` route. No API contract change.
- Dependencies: none new (pymupdf already renders pages; shade is measured from rendered pixels).
