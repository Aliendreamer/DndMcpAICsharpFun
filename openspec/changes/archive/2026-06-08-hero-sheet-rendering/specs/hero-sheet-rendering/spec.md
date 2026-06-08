## ADDED Requirements

### Requirement: Hero detail renders the full character sheet
The hero detail page SHALL render the hero's current `CharacterSheet` organized into sections: identity (race, class/subclass, level, background, alignment, XP), ability scores, combat (HP, AC, speed, initiative, proficiency bonus), proficiencies & languages, features & traits, and equipment. Populated list fields (proficiencies, languages, skills, features, equipment) SHALL be displayed; empty optional groups MAY be omitted.

#### Scenario: Sheet sections render for a hero
- **WHEN** a hero with a populated `CharacterSheet` is opened
- **THEN** the identity, ability scores, combat, proficiencies & languages, features & traits, and equipment sections are shown with the sheet's values

#### Scenario: Spellcasting section is conditional
- **WHEN** the sheet has a non-empty spellcasting ability
- **THEN** a spellcasting section is shown with save DC, attack bonus, per-level spell slots, and known spells
- **AND WHEN** the sheet has no spellcasting ability and the page is not in edit mode
- **THEN** the spellcasting section is not shown

### Requirement: Hero sheet supports view and edit modes
The page SHALL provide a view/edit toggle. In edit mode the sheet fields SHALL be editable and bound to an edit buffer; saving SHALL persist the edited sheet and exit edit mode.

#### Scenario: Edit and save
- **WHEN** the user enters edit mode, changes sheet fields, and saves
- **THEN** the edited values are persisted and the page returns to view mode showing the new values

### Requirement: Hero sheet supports viewing snapshots
When a hero has snapshot history, the page SHALL allow viewing a prior snapshot's sheet without altering the current sheet.

#### Scenario: View a prior snapshot
- **WHEN** the user selects a prior snapshot
- **THEN** the sheet renders that snapshot's values and the current sheet is unchanged
