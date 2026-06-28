# Class/Race NAME candidate-generation gap (PHB) — PARSER issue, not the gate

Confirmed by the full PHB allowlist validation (2026-06-28, `phb14.json` 382 entities + `phb14.declined.json` 587).

## Scope (exact)
- **Classes: 4/12 extracted** (Bard, Ranger, Sorcerer, Warlock). MISSING: Barbarian, Cleric, Druid, Fighter, Monk, Paladin, Rogue, Wizard.
- **Races: 1/9 extracted** (Halfling). MISSING: Dwarf, Elf, Human, Dragonborn, Gnome, Half-Elf, Half-Orc, Tiefling.
- **Deities (God): 0** (PHB deities are TABULAR, not headings → separate tabular-extraction gap).

## Root cause = the PARSER (Marker), NOT the allowlist gate
- The missing class/race names are **"never a candidate"** — they are NOT in `declined.json` (gate never saw them) and NOT in canonical. They never entered the pipeline.
- Proof it's heading-detection: **Spells extract perfectly** (Fireball/Magic Missile/Counterspell/Wish/Mage Armor/Cure Wounds/Eldritch Blast all ✅, 266 total) — Marker emits a clean heading per spell. But it mangles the big class/race section headers, emitting only 4/12 + 1/9. So `EntityCandidateScanner` (which keys off Marker `items` headings) never gets them.
- All 12 classes + 9 races ARE in the 5etools index → they'd `Force(Class/Race)` and extract the moment they become candidates.

## Fix = the parser upgrade
This is exactly what `mem:project_parser_upgrade_mineru` (Marker → MinerU + local OCR) addresses: better heading/layout detection surfaces the missing class/race name-headings. After the swap, re-convert + re-extract → expect 12/12 classes, 9/9 races. The allowlist gate + 5etools match stay unchanged (they already work; spells prove it).

## NOT a regression
Pre-fix `phb14.json` had 0/12 real class names too (its 397 "Class" were all features). So this change strictly improved classes (0→4) and is innocent of the gap.
