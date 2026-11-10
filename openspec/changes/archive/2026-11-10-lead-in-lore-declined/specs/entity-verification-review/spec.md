## ADDED Requirements

### Requirement: Lead-in-only Lore is declined

A programmatic `Lore` entity whose entries are only an introduction sentence or a back-cover blurb SHALL get disposition
`Declined` and `needsReview: false`, regardless of its checks or approval. It MUST NOT be ingested into `dnd_entities` and
MUST NOT be listed as flagged on the review page.

#### Scenario: Introduction sentence

- **WHEN** a Lore entity's only entry is "This section presents one new subclass option: the Path of the Giant for the barbarian."
- **THEN** its disposition is `Declined`

#### Scenario: Back-cover blurb

- **WHEN** a Lore entity's only entry ends "for the world's greatest roleplaying game"
- **THEN** its disposition is `Declined`

#### Scenario: One-sentence rule is kept

- **WHEN** a Lore entity's only entry is "You gain one 9th-level spell slot, provided that you already have one."
- **THEN** its disposition is derived from its checks as before

#### Scenario: Longer text is kept

- **WHEN** a Lore entity's entries hold more than one sentence
- **THEN** its disposition is derived from its checks as before
