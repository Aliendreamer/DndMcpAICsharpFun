## ADDED Requirements

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
