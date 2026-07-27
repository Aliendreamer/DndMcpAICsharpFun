## ADDED Requirements

### Requirement: A section or category heading SHALL be rejected before the recovery LLM call

The decline-recovery pass SHALL apply a deterministic name-shape guard **before** issuing its
recovery-framed union call. A candidate whose name is a section or category heading SHALL be left
declined and SHALL NOT be re-offered to the model.

This does not replace the existing "true noise stays declined" requirement — it stops that requirement
resting solely on the model selecting `none`. On Guildmasters' Guide to Ravnica the model selected
`Rule`/`Lore` for 97 such headings, producing 34% of the book's entities as non-entities.

The guard SHALL reject:

- a **category-noun suffix**: the name ends in one of the observed section-heading nouns —
  `Creatures`, `Items`, `NPCs`, `Spells`, `Actions`, `Lairs`, `Option`, `Options`, `Features`,
  `Proficiencies`, `Descriptions`, `Properties`, `Traits`, `Personality`, `Increases`, `Bestiary`.
  The vocabulary is empirical, drawn from all three books' actual recovery output (GGR's 86 plus the
  heading-shaped recoveries in EGW and SCC), not from one book's shape
- a **bare creature-type plural**: the whole name is one or two D&D creature-type plurals, optionally
  joined by `and` — `Aberrations`, `Beasts`, `Demons`, `Giants`, `Humanoids`, `Monstrosities`,
  `Undead`, `Vampires`, `Zombies`, `Insects and Arachnids`
- a **book-structure heading**: `Chapter <n>[: …]`, `Bestiary`, or `Appendix …`
- a **truncated possessive**: a name consisting only of `<Name>'s`

The guard SHALL be scoped to the recovery pass and SHALL NOT alter the main extraction gate, the
grounding cascade, or the disposition policy.

#### Scenario: A guild/category section heading is not re-offered

- **WHEN** a declined candidate is named `Boros Creatures`, `Golgari NPCs`, `Azorius Items`, or
  `Domain Spells`
- **THEN** the guard rejects it, no recovery LLM call is made, and it stays declined

#### Scenario: A bare creature-type plural is not re-offered

- **WHEN** a declined candidate is named `Zombies`, `Aberrations`, or `Insects and Arachnids`
- **THEN** the guard rejects it and it stays declined

#### Scenario: A book-structure heading is not re-offered

- **WHEN** a declined candidate is named `Chapter 5: Treasures` or `Bestiary`
- **THEN** the guard rejects it and it stays declined

#### Scenario: A truncated possessive fragment is not re-offered

- **WHEN** a declined candidate is named `Aurelia's` or `Trostani's` (a heading cut off after the
  possessive)
- **THEN** the guard rejects it and it stays declined

#### Scenario: Genuine rule and lore prose is still recovered

- **WHEN** a declined candidate is a real mechanical rule or worldbuilding passage whose name is not a
  heading shape — for example `Psychic Wind Effects`, `Loose Pantheons`, or a named NPC such as
  `Teysa Karlov` or `Mizzix of the Izmagnus`
- **THEN** the guard passes it through and the existing recovery behaviour is unchanged

#### Scenario: A singular creature name is not mistaken for a category

- **WHEN** a declined candidate is named `Zombie` or `Giant Insect` (singular — a real creature)
- **THEN** the guard does NOT reject it on the bare-plural rule

### Requirement: Guarded rejections SHALL be logged

Each candidate the guard rejects SHALL be logged with its name, so the pass's suppressions are visible
rather than silent. A recovery pass that quietly drops candidates is indistinguishable from one that
found nothing to recover.

#### Scenario: A rejection is traceable

- **WHEN** the guard rejects a declined candidate
- **THEN** a log entry records the candidate name and that the recovery was skipped by the name guard
