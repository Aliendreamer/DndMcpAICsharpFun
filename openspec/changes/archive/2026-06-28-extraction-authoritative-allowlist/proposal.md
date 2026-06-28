## Why

The live PHB re-run (2026-06-28) confirmed the merged `extraction-name-resolution` fix recovered
recall (Bard→Class, appendix animals→Monster, clean canonical names) but exposed a dominant,
PRE-EXISTING precision problem: of the first 100 PHB candidates only ~22 were real entities; ~78 were
chapter-body noise (class features `Rage`/`Frenzy`, stat-block field labels `Hit Points`/`Ability
Score Increase`/`Age`/`Size`/`Speed`, chapter headings, flavor sidebars, name lists `Calishite`/
`Chondathan`, OCR garble `HIT POlNTS`). The prior `phb14.json` had 771 entities including **397 "Class"**
(PHB has 12) and 243 "Spell". The content-first fallback never declines this noise, and these sub-
headers are deliberately absent from 5etools — so they leak through. This output is unfit to ingest.

5etools is a COMPLETE authoritative list for the well-covered entity types. We can use it as an
allowlist: for an official book, a candidate of a well-covered type that does not match 5etools is
noise and should be declined — deterministically, with no LLM call.

## What Changes

- For **official books** (the book has a `FivetoolsSourceKey`), treat 5etools as an authoritative
  allowlist for **8 gated types**: Spell, Monster, Class, Race, Background, Feat, Condition, God.
- Extend `DeterministicTypeResolver` with a new **`Decline`** outcome. Revised ladder: 5etools match →
  `Force(type, canonical)`; else complete stat block → `Force(Monster)` (rescue guard **always wins**,
  even official — never drop a real monster); else official + prior type(s) **all** gated + no match →
  **`Decline(no_5etools_match)`** (no LLM); else → existing drop-filter / content-first (unchanged).
- Declined candidates are written to a NEW sibling file `books/canonical/<book-slug>.declined.json`
  (`{id, name, type, reason}`), separate from `errors.json` so the `errorsOnly` retry ignores them.
  Main `entities` stay clean.
- **Ungated** (content-first unchanged): Item, MagicItem, Plane (Plane has no source array in the
  mirror and cannot be gated; Item/MagicItem coverage is uncertain).
- Homebrew books (no `FivetoolsSourceKey`) are unaffected — the gate never fires.

## Capabilities

### New Capabilities
- `authoritative-allowlist`: the official-book 5etools allowlist gate (gated-type set, `isOfficial`
  determination, the decline-on-no-match rule, and the `<book-slug>.declined.json` output).

### Modified Capabilities
- `deterministic-type-resolution`: the resolver gains a `Decline` outcome and the `isOfficial` +
  gated-type-set inputs; the match ladder inserts the allowlist gate between the stat-block rescue
  guard and the existing drop/content-first fallback.

## Impact

- Code: `Features/Ingestion/EntityExtraction/DeterministicTypeResolver.cs` (new outcome + ladder),
  `EntityExtractionOrchestrator.cs` (pass `isOfficial`; collect + write declined records),
  a new declined-records writer, DI/options for the gated-type set.
- Output: new `books/canonical/<book-slug>.declined.json` sibling per official book.
- Docs: `DndMcpAICsharpFun.http` / `dnd-mcp-api.insomnia.json` only if an endpoint changes (none
  planned); CLAUDE.md note on the new sibling file.
- Validation: live PHB re-run is the acceptance gate (expect Class 397→~12, race fields gone,
  declined.json populated, recall + real entities intact). No DB schema or API contract changes.
