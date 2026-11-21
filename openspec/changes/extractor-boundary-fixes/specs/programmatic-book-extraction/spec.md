# Spec Delta

## ADDED Requirements

### Requirement: Table continuations join their table

A table whose rows continue past a column or page break SHALL stay one table in the entity that holds its start. A
repeated column header at the top of the continuation (including OCR misreads of `d100 Effect` such as "Oolp Effect")
MUST NOT start a new entity.

#### Scenario: Haunted Effects across a column break

- **WHEN** TCE's Haunted Effects d100 table continues into the next column under a repeated "d100 Effect" header
- **THEN** `tce.rule.haunted` holds rows 01-00 and no `tce.rule.haunted-oolp-effect-*` entity is produced

#### Scenario: Table across a page break

- **WHEN** the Unraveling Magic Effects table runs from one page onto the next
- **THEN** one entity holds every row and no fragment entity is produced

### Requirement: Table rows are not headings

A line that is a row of a table (it starts with the row's number or range under that table's header) MUST NOT be read
as an entity heading, whatever words it contains.

#### Scenario: Contact row mentioning a weapon

- **WHEN** GGR p69's Non-Izzet Contacts row reads "5 I helped a minor Gruul chieftain acquire an Izzet weapon."
- **THEN** no MagicItem is produced from it and the row stays in the Non-Izzet Contacts table

### Requirement: Stat block ends at its last entry

A monster stat block SHALL end after its last trait, action, bonus action, reaction or legendary action entry. Following
prose, other entities' text and later pages MUST NOT be absorbed into it, and trait or action text MUST NOT be parsed
into `languages` or another header field.

#### Scenario: Homunculus Servant stops at Channel Magic

- **WHEN** ERLW's Homunculus Servant stat block is followed by the Replicate Magic Item infusion
- **THEN** the monster's text ends after Channel Magic and its traits and actions are in `trait`, `action` and `reaction`

#### Scenario: Sacred Statue languages

- **WHEN** a Sacred Statue's Languages line is followed by its traits
- **THEN** `languages` holds only "the languages the eidolon knew in life"

### Requirement: Both columns of a stat block are read

A stat block that runs from the left column into the right column, or across a page, SHALL be read in full, left column
then right column. Its text MUST NOT end mid-sentence where a column ends.

#### Scenario: Adult dragon right column

- **WHEN** the Adult Gold Dragon's stat block continues in the right column with Breath Weapons and Legendary Actions
- **THEN** the entity's text and fields include Breath Weapons, Change Shape and its legendary actions

#### Scenario: Text does not stop mid-sentence

- **WHEN** a stat block's left column ends inside Frightful Presence
- **THEN** the entity continues with the rest of that sentence from the right column

### Requirement: Sidebars and quotes stay out

A sidebar, boxed text or pull quote printed beside or inside an entity's section MUST NOT be glued into that entity's
text or fields. One with its own heading SHALL become its own entity; a headingless quote box is left to block retrieval.

#### Scenario: Werewolf quote

- **WHEN** an all-caps quote box sits inside the Werewolf's stat block area
- **THEN** the Shapechanger trait holds only its own text and the Werewolf has its actions

#### Scenario: Schools of Magic sidebar

- **WHEN** PHB's "The Schools of Magic" sidebar sits next to Concentration
- **THEN** Concentration's text does not contain it

### Requirement: Sibling tables are separate entities

Tables that each carry their own printed title under a common heading SHALL each become their own entity named by that
title, instead of running into the previous table's entity.

#### Scenario: XGE random encounters

- **WHEN** XGE prints Arctic Encounters (Levels 1-4) through (Levels 17-20) one after another
- **THEN** each of those tables is its own entity named by its title

#### Scenario: Monsters' Desires

- **WHEN** TCE prints d4 desire tables for Aberrations, Beasts, Celestials and others under Monsters' Desires
- **THEN** each table is its own entity

### Requirement: Boundary fixes keep all content

Text that a boundary rule detaches from an entity SHALL end up in another entity: its own heading's section as a Lore or
Rule entity, or the table or entity it belongs to. A boundary rule MUST NOT be the reason printed body text appears in
no entity; headingless quote boxes are not body text.

#### Scenario: Izzet sections survive

- **WHEN** the Gruul row is no longer read as a MagicItem heading
- **THEN** "How Do I Fit In?", "An Izzet Party" and "Rank and Renown" are each in a GGR entity

#### Scenario: Artificer infusions survive

- **WHEN** the Homunculus Servant stops at Channel Magic
- **THEN** Replicate Magic Item and Group Patrons are each in an ERLW entity
