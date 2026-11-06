## Why

Switching all 16 books to the programmatic extractor (Phase 1) removed every entity type it does not produce. That
includes the 12 PHB classes and every race, subclass, feat and background. The character coach (level-up advice,
concept-to-build recommender, build critique) reads those entities directly, so it is running without data. The owner's
end goal is every book data-mined into correct, well-shaped entities of every type. Phase 2a delivers the character
data first.

## What Changes

- The book extractor gains deterministic parsers for **Class, Subclass, Race, Subrace, Feat and Background**, run on
  all 16 registered books from the existing OCR cache (CPU only, no LLM, no 5etools at runtime).
- A table reader for class level tables (rows and columns from OCR line boxes). Phase 1 has none.
- Per-type self-checks feed the existing D4 gate: an entity passes only if every check passes, and anything else goes
  to NeedsReview. Examples: hit die agrees across its three printed places; proficiency bonus steps; table features
  match feature headings; subclass feature levels match the class; ability increases, size and speed parse; real skill
  names.
- Output in the existing `ClassFields`, `SubclassFields`, `RaceFields`, `SubraceFields`, `FeatFields` and
  `BackgroundFields` shapes. Class features are carried as the 5etools-style refs the coach already parses, so the
  coach code does not change.
- The development scorer gains 5etools truth for these six types (`class/`, `races.json`, `feats.json`,
  `backgrounds.json`).
- The C# side confirms each of the six types renders canonical text, ingests, and shows its fields on `/review`, and
  fills any gap.
- Live re-run of all 16 books once the done bar is met.

## Capabilities

### New Capabilities

- `programmatic-character-extraction`: deterministic parsing and self-checks for Class, Subclass, Race, Subrace, Feat
  and Background, including the class level-table reader.

### Modified Capabilities

- `programmatic-book-extraction`: the extractor service now produces the six character types in addition to Monster,
  Spell and MagicItem. The run-endpoint allow-list covers all 16 validated books. The development scorer covers the new
  types.

## Impact

- `Tools/BookExtractor/bookextractor/` gets new parsers, a table reader, checks, score truth and fixtures.
- `Features/Entities/CanonicalText/*`, `Features/Ingestion/Programmatic/*` and the `/review` page get gap fixes only.
- Canonical files for all 16 books gain these types, and the coach gets class, race and feat data again.
- Done bar, owner-approved: zero unexplained wrong-and-accepted entities, and at least 90% of each book's 5etools
  entries for the six types found.
