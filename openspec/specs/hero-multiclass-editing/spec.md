# hero-multiclass-editing Specification

## Purpose
TBD - created by archiving change hero-multiclass-editing. Update Purpose after archive.
## Requirements
### Requirement: The class-name list is a single public source

`MulticlassRules` SHALL expose a public `KnownClasses` list of the 13 supported class names, and it SHALL contain exactly the keys of the multiclass prerequisite/proficiency maps so the editor dropdown and the rules engine can never diverge.

#### Scenario: KnownClasses matches the rules maps

- **WHEN** `MulticlassRules.KnownClasses` is enumerated
- **THEN** it contains the 13 class names (Barbarian, Bard, Cleric, Druid, Fighter, Monk, Paladin, Ranger, Rogue, Sorcerer, Warlock, Wizard, Artificer) and each entry resolves in `CanMulticlassInto` and `MulticlassProficiencies`

### Requirement: The hero editor edits a per-class list without collapsing classes

The `HeroDetail` edit form SHALL edit the character's classes as a repeatable list bound to `CharacterSheet.Classes` (each row: class, level, subclass; with add and remove), and saving SHALL preserve every class entry. Saving SHALL NOT collapse a multiclass character to a single class.

#### Scenario: A multiclass hero survives an edit-and-save

- **WHEN** a hero with two class entries (e.g. Rogue 3 / Fighter 2) is opened in the edit form and saved without touching the class rows
- **THEN** the saved snapshot still has both class entries (no `SetSingleClass` collapse)

#### Scenario: A class can be added and removed

- **WHEN** the user clicks "Add class", picks a class and level, then later removes a row
- **THEN** the `Classes` list gains and loses those entries accordingly, and the total level reflects the current rows

#### Scenario: The class name is chosen from the known classes

- **WHEN** a class row's class is selected
- **THEN** it is chosen from `MulticlassRules.KnownClasses` (a dropdown), so it is always a valid key for the rules engine

### Requirement: The editor shows live derived level and proficiency bonus

The edit form SHALL display a read-only total level and proficiency bonus derived from the in-progress class list, recomputed as the rows change.

#### Scenario: Total level and proficiency bonus update as levels change

- **WHEN** the class rows sum to total level 5
- **THEN** the editor shows total level 5 and proficiency bonus +3, and changing a row's level updates both without a save

### Requirement: The editor shows a non-blocking multiclass validity and proficiency advisory

For each non-primary class row, the edit form SHALL show — computed from the in-progress sheet's ability scores via the deterministic multiclass rules — whether that class's multiclass prerequisite is met (with the failed reason when not) and the reduced proficiency subset it grants. The advisory SHALL NOT block or prevent saving.

#### Scenario: A failed prerequisite is surfaced but does not block save

- **WHEN** a second class row is a class whose ability-score prerequisite the current scores do not meet
- **THEN** that row shows a "not allowed" advisory with the failed prerequisite, and the user can still save the hero

#### Scenario: The primary class shows no prerequisite advisory

- **WHEN** the class rows are displayed
- **THEN** the first (primary) row shows no multiclass-prerequisite advisory, because the primary class has no multiclass-into requirement

### Requirement: View mode lists all classes

The `HeroDetail` view (non-edit) mode SHALL present all of a character's classes, not only the primary class.

#### Scenario: A multiclass hero is displayed with every class

- **WHEN** a Rogue 3 / Fighter 2 hero is viewed
- **THEN** both classes and their levels are shown, along with the total character level

