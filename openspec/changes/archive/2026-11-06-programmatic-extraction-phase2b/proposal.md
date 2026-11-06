## Why

Phase 2a brought character data back to the chat from the programmatic extractor. The owner's goal is every book
mined into correct entities of every type. Equipment and hazards are the next group, and they are still missing:
weapons, armor, adventuring gear, tools, mounts and vehicles, siege objects, traps and hazards, diseases and poisons,
and conditions. Encounter design, crafting, downtime and rules answers all ask about them.

## What Changes

- The book extractor gains deterministic parsers for **Weapon, Armor, Item (mundane gear, tools, packs, mounts, trade
  goods), VehicleMount, Object, Trap (traps and hazards), DiseasePoison and Condition**. They run on all 16 registered
  books from the OCR cache, on CPU, with no LLM and no 5etools at runtime.
- Most of these types are printed as tables (PHB weapons, armor and gear) or short fixed blocks (traps, poisons,
  conditions). The Phase 2a table reader is generalised beyond class level tables.
- Per-type self-checks feed the existing gate: an entity is Accepted only if every check passes, otherwise it goes to
  NeedsReview. Examples:
  - cost carries a valid unit;
  - damage dice and AC formulas are valid;
  - weapon properties come from a closed set;
  - trap DCs parse;
  - poison type comes from a closed set;
  - condition names come from a closed set.
- Output uses the existing 5etools-shaped `*Fields` schemas.
- The dev scorer gains 5etools truth for these types (`items-base.json`, `items.json` mundane, `vehicles.json`,
  `objects.json`, `trapshazards.json`, `conditionsdiseases.json`) and per-type text precision.
- The C# side confirms that each type renders, ingests and shows on `/review`.
- All 16 books are run live, then verified in `dnd_entities`.

## Capabilities

### New Capabilities

- `programmatic-equipment-extraction`: deterministic parsing and self-checks for Weapon, Armor, Item, VehicleMount,
  Object, Trap, DiseasePoison and Condition, including table-row entities.

### Modified Capabilities

- `programmatic-book-extraction`: the extractor service produces these eight types, and the development scorer covers
  them.

## Impact

- `Tools/BookExtractor/bookextractor/`: new parsers, a generalised table reader, checks, scorer truth and fixtures.
- C# gap fixes only, if any.
- Canonical files for all 16 books gain these types.
- Done bar (owner-approved for Phase 2): zero unexplained wrong-and-accepted values, and at least 90% of each book's
  5etools entries per type found.
