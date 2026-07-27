# entity-name-resolution Specification

## Purpose
TBD - created by archiving change extraction-name-resolution. Update Purpose after archive.
## Requirements
### Requirement: 5etools entity-name index

The system SHALL build an in-memory index from the local 5etools data (`5etools/*.json`) mapping a
normalized entity name to its canonical name and `EntityType`. The index SHALL cover TOP-LEVEL entity
types only — spell, monster, item, class, background, race, feat, condition, deity (→ God) — and SHALL
NOT index optionalfeatures or subclass-features (which are sub-features, not standalone entities).
(Planes have no standalone source array in the local 5etools mirror, so they are not indexed and are
covered by the content-first fallback.) The index SHALL be built once (singleton) at startup.

#### Scenario: Top-level entities are indexed
- **WHEN** the index is built
- **THEN** "Fireball" resolves to (canonical "Fireball", Spell), "Aboleth" to (canonical "Aboleth", Monster), and "Bard" to (canonical "Bard", Class)

#### Scenario: Sub-features are not indexed
- **WHEN** the index is built
- **THEN** an optional-feature / class-feature name like "Spellcasting" or "Archery" is NOT in the index (so it cannot be matched as a standalone entity)

#### Scenario: Monsters match across all sources
- **WHEN** a candidate "Lion" (printed in the PHB appendix but catalogued by 5etools under MM) is matched
- **THEN** it resolves to (canonical "Lion", Monster) via the full-corpus monster index, not a per-book subset

### Requirement: 5etools type mapping

The index SHALL map each 5etools type to the project `EntityType`. A 5etools `item` with a rarity
that denotes a magic item SHALL map to `MagicItem`; a mundane item SHALL map to `Item`.

#### Scenario: Magic item vs mundane item
- **WHEN** indexing 5etools items
- **THEN** "Bag of Holding" (has a rarity) maps to `MagicItem` and a mundane item (no rarity) maps to `Item`

### Requirement: Candidate-name matching with fuzzy threshold

`EntityNameMatcher.Match(rawCandidateName)` SHALL normalize the raw heading (case/punctuation) and
return the canonical name + `EntityType` of the best index match, or null. It SHALL try an exact
normalized match first, then a fuzzy match accepted only above a confidence threshold (so OCR-merged
headings resolve, but a wrong neighbour does not). A null result SHALL NOT drop the candidate.

#### Scenario: All-caps heading matches exactly
- **WHEN** `Match("FIREBALL")` is called
- **THEN** it returns (canonical "Fireball", Spell)

#### Scenario: OCR-merged heading matches by fuzzy threshold
- **WHEN** `Match("MAGEARMOR")` is called (OCR dropped the space)
- **THEN** it returns (canonical "Mage Armor", Spell) because the fuzzy match is above the confidence threshold

#### Scenario: Non-entity heading does not match
- **WHEN** `Match("ACTIONS")` or `Match("A RED DRAGON'S LAIR")` is called
- **THEN** it returns null (no index entry within the threshold)

#### Scenario: Ambiguous/low-confidence does not match
- **WHEN** a raw heading is below the fuzzy confidence threshold for every index entry
- **THEN** `Match` returns null (the candidate is NOT given a wrong canonical name — it falls through to content-first)

### Requirement: Candidate-name matching SHALL strip a trailing magic-item stat line before lookup

`EntityNameMatcher` SHALL strip a trailing magic-item stat line from the raw candidate name before normalising and looking it up in the 5etools index, in addition to the existing monster stat-line strip. Without this, a concatenated heading such as `"AMULET OF THE DRUNKARD Wondrous item, uncommon"` normalises to a key that exists in no index and matches nothing, so the candidate loses its grounding and its stable entity id.

Both strips SHALL be applied, and the strip SHALL run **before** the match, because `DeterministicTypeResolver` never drops a name that matches a 5etools entry — a match found on the stripped name is what protects a real magic item from being dropped by the name gate.

#### Scenario: A concatenated magic-item heading grounds to its 5etools entry

- **WHEN** the raw candidate name is `"AMULET OF THE DRUNKARD Wondrous item, uncommon"` and 5etools contains the `Amulet of the Drunkard` magic item
- **THEN** the matcher strips the trailing stat line and returns that entry, so the candidate is grounded and receives the canonical name and a stable id

#### Scenario: Monster stat-line stripping is unaffected

- **WHEN** the raw candidate name carries a monster stat-line suffix, e.g. `"Moorbounder Large monstrosity, unaligned"`
- **THEN** the existing monster strip still applies and the name resolves as before

#### Scenario: A name that is only a stat line matches nothing

- **WHEN** the raw candidate name is `"Wondrous item, uncommon"`
- **THEN** stripping leaves the input unchanged, the lookup finds no entry, and the candidate is left unmatched so the name gate can drop it

### Requirement: The 5etools name index SHALL carry each entry's source

Every entry in `EntityNameIndex` SHALL record the 5etools `source` value it was loaded from, alongside
its canonical name and entity type. Without the source on the entry, a caller cannot express a
preference for the book currently being extracted, and a heading is typed by whichever file happened to
load first.

#### Scenario: An entry exposes its source

- **WHEN** the index loads the EGW `Orc` race entry and a bestiary `Orc` monster entry
- **THEN** both entries are retained for the normalised name `orc`, each carrying its own source
  (`EGW` and the bestiary source respectively) in addition to its type

### Requirement: The 5etools name index SHALL index subraces

`EntityNameIndex` SHALL load the `subrace` array from `races.json` as `EntityType.Subrace`. Subraces are
currently not indexed at all, so subrace headings (`Draconblood`, `Ravenite`, `Lotusden`, `Pallid`)
cannot be matched, grounded, or recovered by any caller.

Subrace entries SHALL be loaded after base races, consistent with the existing convention by which a
colliding name resolves to the broader type first (as base classes are loaded before subclasses).

#### Scenario: A subrace heading matches

- **WHEN** a candidate name is `"Draconblood"` or `"Pallid"` and `races.json` contains that subrace
- **THEN** the matcher returns the entry with type `Subrace`

#### Scenario: A subrace name colliding with a race resolves to the race first

- **WHEN** a normalised name exists as both a race and a subrace
- **THEN** the first-wins ordering resolves it to the `Race` entry, matching the existing
  class-before-subclass convention

### Requirement: Matching SHALL support same-source preference with global fallback

`EntityNameMatcher` SHALL expose a lookup that prefers entries whose source equals a caller-supplied
source key, and falls back to the global (first-wins) result when no same-source entry matches at the
confidence bar. The existing untyped `Match` and type-restricted `MatchOfType` behaviour SHALL be
unchanged, so existing callers are unaffected.

The same-source lookup SHALL apply the established confidence bar (exact normalised hit, then bounded
fuzzy at the existing threshold) — it changes which candidate entries are considered, not how
confidently a name must match.

#### Scenario: Same-source entry is preferred

- **WHEN** the caller supplies source key `EGW` and the name `"Orc"` matches both an `EGW` race entry
  and a monster entry from another source
- **THEN** the `EGW` race entry is returned

#### Scenario: Falls back to global when the book has no such entry

- **WHEN** the caller supplies source key `EGW` and the name matches no `EGW`-sourced entry but does
  match an entry from another source at the confidence bar
- **THEN** that entry is returned

#### Scenario: No match at the confidence bar returns nothing

- **WHEN** neither a same-source nor a global entry matches at the confidence bar
- **THEN** the lookup returns null and the caller's non-match path applies unchanged

#### Scenario: Existing lookups are unchanged

- **WHEN** an existing caller uses `Match` or `MatchOfType`
- **THEN** results are identical to those before this change

