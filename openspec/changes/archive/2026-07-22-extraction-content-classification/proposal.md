## Why

Extraction is effectively binary: a candidate is either a typed **Entity** (kept, structured) or **declined** (dropped from the structured layer — only raw prose survives in `dnd_blocks`). But *everything* in a book is something — a **Rule** ("Switching Weapons"), a **Lore** passage (deity pantheons), a **Table** (Draconic Ancestry), a **Variant/Sidebar** ("Variant: …") — and today we correctly recognize "not a monster" but never record **what it actually is**. The high decline rate on rules/guidance books (DMG ~50%) is *correct* as anti-fabrication, but it throws away the semantic classification of most of the book. We should MAP each candidate to its true category, not collapse the whole non-entity world into `none`.

## What Changes

- Extend the extraction classification target from `{entity types} ∪ none` to **`{entity types} ∪ {non-entity categories} ∪ {true structural noise}`**. Non-entity categories to model first: **Rule**, **Lore**, **Table** (link to the `CanonicalTable` from mineru-table-extraction), **Variant/Sidebar**. Only genuine TOC / chapter-header / fragment noise is *declined*; everything else is classified and kept as its category with provenance.
- Reconcile with the existing block-level `ContentCategory` + `ask_rules`/`ask_setting_lore` (which already categorize prose) so the entity layer and the retrieval layer share one taxonomy.

## Capabilities

### New Capabilities

- `extraction-content-classification`: every scanned candidate is mapped to what it is — a typed entity, or a recognized non-entity category (Rule / Lore / Table / Variant), or true noise — rather than binary entity-or-declined, so the book's non-entity content is structurally understood, not discarded.

### Modified Capabilities

<!-- generalizes extraction-cross-type-recovery (its rules-policy item is a subset); touches the discriminated-union decoding + disposition on implementation -->

## Impact

- The content-first discriminated union / `deterministic-type-resolution` / `extraction-disposition` — add non-entity category branches; the decline set shrinks to true noise.
- Domain: a lightweight non-entity record (category + prose + provenance), or reuse the block `ContentCategory` mapping — decided on design.
- Large, foundational — likely phased (Rule first, then Lore/Variant); subsumes the rules-policy half of `extraction-cross-type-recovery` (which stays for the item re-typing half).
- Deferred; not part of the running corpus table re-extraction.
