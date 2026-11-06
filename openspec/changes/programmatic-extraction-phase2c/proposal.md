## Why

The owner's Phase 2 goal is every book data-mined into correct entities of every type. Phases 2a and 2b delivered
character data and equipment/hazards. The last group is the world: gods, planes, factions, locations, lore and rules.
Lore and setting answers (`ask_setting_lore`, `ask_rules`) and campaign prep draw on them.

## What Changes

- **God:** deity table rows (PHB Appendix B pantheons, DMG, SCAG) and deity prose sections (MTF, EGW, ERLW, VRGR) in
  `GodFields` (alignment, domains, symbol, pantheon, title/province), checked against closed sets.
- **Rule:** named rule sections (variant and optional rules, downtime activities, rules chapters' named subsections) in
  `RuleFields` with `ruleType` where the print marks it (Variant/Optional).
- **Plane, Faction, Location, Lore:** heading-bounded sections from the books' setting chapters, typed by the printed
  chapter or section that encloses them, each section's text complete and bleed-free.
- Per-type self-checks feed D4; scorer truth from `deities.json`, `variantrules.json`, and the per-book section tree
  `5etools/book/book-<key>.json` for the section types; text precision for every type.
- C# render/ingest/review confirmation; final whole-change review before a live run of all 16 books; `dnd_entities`
  verified per book.

## Capabilities

### New Capabilities

- `programmatic-world-extraction`: deterministic parsing and self-checks for God, Rule, Plane, Faction, Location and
  Lore.

### Modified Capabilities

- `programmatic-book-extraction`: the service produces the six world types; the scorer covers them.

## Impact

- `Tools/BookExtractor/bookextractor/`: new parsers, a shared section-tree reader, checks, scorer truth, fixtures.
- Canonical files for all 16 books gain the world types.
- Done bar (owner-approved for Phase 2): zero unexplained wrong-and-accepted values; ≥90% of each book's 5etools
  entries per type found (God, Rule), and for section types ≥90% of the book's section-tree headings at the chosen
  level inside the typed chapters.
