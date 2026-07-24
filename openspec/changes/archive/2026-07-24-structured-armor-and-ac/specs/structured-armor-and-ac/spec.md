## ADDED Requirements

### Requirement: Characters carry a structured worn-armor selection

`CharacterSheet` SHALL carry a structured `WornArmor` value with an armor name, a shield flag, and a magic bonus, persisted in the existing JSON sheet column (no migration). An armor name that is empty or `"None"` SHALL mean unarmored. A snapshot saved before this field existed SHALL deserialize to a non-null unarmored default. The existing manual `ArmorClass` integer SHALL be unchanged and SHALL NOT be read or written by the armor-class resolver.

#### Scenario: Old snapshot deserializes to unarmored

- **WHEN** a hero snapshot saved before `WornArmor` existed is loaded
- **THEN** its `WornArmor` is a non-null unarmored default, and resolving `armor class` treats it as unarmored (does not throw)

#### Scenario: Manual ArmorClass is independent

- **WHEN** the `armor class` resolver computes a value
- **THEN** it neither reads nor writes `CharacterSheet.ArmorClass` (the two are independent)

### Requirement: A static armor catalog supplies base AC and category

The system SHALL provide a static catalog mapping each PHB armor name to its base AC and category (Light, Medium, or Heavy), looked up case-insensitively. An armor name that is not in the catalog and not unarmored SHALL cause the armor-class resolver to return `needsReview` rather than a fabricated base AC.

#### Scenario: Catalog supplies base AC and category

- **WHEN** the resolver looks up `"Chain Mail"`
- **THEN** it gets base AC 16, category Heavy

#### Scenario: Unknown armor is needsReview, not fabricated

- **WHEN** `WornArmor.ArmorName` is a name not in the catalog (and not unarmored)
- **THEN** the `armor class` fact resolves to `needsReview` with no fabricated base AC

### Requirement: Resolve armor class deterministically from worn armor and abilities

`CharacterResolutionService` SHALL resolve an `armor class` feature as a pure computation (no database). Unarmored SHALL be `10 + Dex modifier`, raised to the best applicable Unarmored Defense for the character's classes (Barbarian `10 + Dex + Con`, shield permitted; Monk `10 + Dex + Wis`, only when no shield is worn). Light armor SHALL be `base + Dex modifier`; Medium `base + min(Dex modifier, 2)`; Heavy `base`. A shield SHALL add `+2` and the magic bonus SHALL add its value. The result SHALL be a `ResolvedFact` whose components enumerate the contributions and whose value is the total; computed values SHALL carry no provenance.

#### Scenario: Heavy armor ignores Dexterity

- **WHEN** a character wears Plate (base 18) with Dexterity 16 (`+3`)
- **THEN** the armor class is 18 (no Dex added)

#### Scenario: Medium armor caps the Dexterity bonus at 2

- **WHEN** a character wears Half Plate (base 15) with Dexterity 18 (`+4`)
- **THEN** the armor class is 17 (base 15 + min(4, 2))

#### Scenario: Light armor adds the full Dexterity bonus

- **WHEN** a character wears Leather (base 11) with Dexterity 16 (`+3`)
- **THEN** the armor class is 14

#### Scenario: Shield and magic bonus add on top

- **WHEN** a character wears Chain Mail (base 16), a shield, and a +1 magic bonus
- **THEN** the armor class is 19 (16 + 2 + 1), with distinct shield and magic components

#### Scenario: Barbarian Unarmored Defense with a shield

- **WHEN** an unarmored Barbarian with Dexterity 14 (`+2`) and Constitution 16 (`+3`) wears a shield
- **THEN** the armor class is 10 + 2 + 3 + 2 (shield) = 17

#### Scenario: Monk Unarmored Defense is suppressed by a shield

- **WHEN** an unarmored Monk with a shield is resolved
- **THEN** Monk Unarmored Defense does not apply, and the armor class falls back to `10 + Dex + 2` (shield)

#### Scenario: Multiclass takes the higher Unarmored Defense

- **WHEN** an unarmored Barbarian/Monk multiclass is resolved
- **THEN** the higher of the Barbarian and (shield-permitting) Monk Unarmored Defense values is used
