# automatic-decline-recovery Specification

## Purpose
TBD - created by archiving change automatic-decline-recovery. Update Purpose after archive.
## Requirements
### Requirement: Still-declined candidates SHALL be automatically recovered as grounded Rule/Lore entities

After the main extraction loop for an official book, the pipeline SHALL run a recovery phase over the still-declined candidates (no separate trigger — inside `extract-entities`). For each, it SHALL issue one recovery-framed union call offering `[Rule, Lore]` and `none`, with a system prompt establishing the content as real, official, non-fabricated and asking for Rule (mechanical), Lore (worldbuilding/setting), or `none` (heading/TOC/fragment). A `Rule`/`Lore` selection that grounds via the existing grounding cascade SHALL be admitted as an entity (`dataSource:"decline-recovery"`, `authority:"canon-unindexed"`) and removed from the decline audit; a `none` selection or an ungrounded result SHALL remain declined. The main extraction gate SHALL be unchanged.

#### Scenario: A wrongly-declined mechanical rule is recovered as Rule

- **WHEN** a candidate like `psychic-wind-effects` was declined by the main extraction but is a real DMG mechanical rule
- **THEN** the recovery phase re-classifies it as `Rule`, and if its fields ground against its text it is admitted as a `Rule` entity and dropped from the decline audit

#### Scenario: Worldbuilding narrative is recovered as Lore

- **WHEN** a declined candidate is real setting/worldbuilding narrative (e.g. `loose-pantheons`)
- **THEN** the recovery phase may classify it as `Lore` and, if grounded, admit it as a `Lore` entity

#### Scenario: True noise stays declined, never fabricated

- **WHEN** a declined candidate is a pure chapter heading / TOC entry / fragment
- **THEN** the recovery selects `none` (or the result fails grounding) and it stays declined — the recovery never fabricates and never lowers the grounding bar

#### Scenario: Homebrew books skip recovery

- **WHEN** the book is not official (no fivetools source key)
- **THEN** the "real by definition" recovery phase is skipped (web-vouch recovery is out of scope)

#### Scenario: Recovery is automatic and additive

- **WHEN** `extract-entities` runs for an official book
- **THEN** the recovery phase runs automatically as the final step, with no separate endpoint or manual trigger, and the main extraction results are unchanged except for the added recovered entities

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

