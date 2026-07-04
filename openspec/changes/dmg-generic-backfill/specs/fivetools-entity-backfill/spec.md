## ADDED Requirements

### Requirement: Type-parameterized 5etools recall check

The system SHALL provide `GET /admin/books/{id}/entity-recall?type={type}` that, for a book with a
`FivetoolsSourceKey`, diffs the book's canonical entities of the requested type against the book's
source-filtered 5etools roster for that type, and reports `present`, `missing`, `extra`, and the
`grounded` vs `backfilled` counts of existing entities. Supported `type` values are `Monster`, `Spell`,
`MagicItem`, and `God`. A book with no `FivetoolsSourceKey` MUST yield an empty no-op result.

#### Scenario: Recall reports gaps for the requested type

- **WHEN** `entity-recall?type=MagicItem` runs for the DMG and the 5etools `DMG` magic-item roster
  contains an item missing from `dmg14.json`
- **THEN** that item name is reported under `missing`, and the response includes the `present`,
  `grounded`, and `backfilled` counts for MagicItem entities

#### Scenario: Unsupported type is rejected

- **WHEN** a recall/backfill/flag request is made with `type=Plane` (or any type outside the supported set)
- **THEN** the endpoint returns HTTP 400 and performs no work

#### Scenario: Homebrew book is a no-op

- **WHEN** the book has no `FivetoolsSourceKey`
- **THEN** the recall check reports nothing and the backfill appends nothing

### Requirement: Type-generic gap-only backfill via per-type providers

The system SHALL provide `POST /admin/books/{id}/backfill-entities?type={type}` that appends any entity
present in the book's authoritative 5etools roster for the requested type but missing (by normalized name)
from its canonical. Each appended entity MUST have its `fields` projected to the canonical `*Fields` shape
for its type (Monster/Spell preserving the existing services' projection; MagicItem→`MagicItemFields`,
God→`GodFields`) and be marked `dataSource: "5etools-backfill"`. The
operation MUST be gap-only (never overwrite or duplicate an existing canonical entity) and idempotent (a
second run with no new gaps changes nothing). A previously extracted (grounded) entity that also exists in
the roster MUST NOT be replaced by a 5etools-sourced one.

#### Scenario: Missing roster entity is backfilled

- **WHEN** the DMG canonical is missing a MagicItem that exists in the 5etools `DMG` roster
- **THEN** the endpoint appends that MagicItem to `dmg14.json` marked `dataSource:"5etools-backfill"`,
  with its fields projected to `MagicItemFields`

#### Scenario: Backfill is gap-only and idempotent

- **WHEN** `backfill-entities?type=Spell` runs and every roster spell is already present in the canonical
- **THEN** the canonical is unchanged and no duplicates are created

#### Scenario: Grounded entity is preserved

- **WHEN** an entity was already extracted from the PDF (grounded) and also exists in the roster
- **THEN** backfill does not replace the grounded entity with a 5etools-sourced one

### Requirement: MagicItem roster is magic items only

The MagicItem provider SHALL read the source-filtered magic items from `items.json`, treating an item as a
magic item when its rarity is present and not `none` (mirroring `FivetoolsMagicItemMapper`'s inclusion
rule). Mundane base items from `items-base.json` MUST NOT be included in the MagicItem roster.

#### Scenario: Base items are excluded from MagicItem recall

- **WHEN** `entity-recall?type=MagicItem` runs for the DMG
- **THEN** mundane base items (rarity none/absent) are not counted as missing or backfilled MagicItems

### Requirement: Recall-check extra entities are categorized

The recall check SHALL split its `extra` set (canonical entities of the requested type absent from the
book's source-filtered 5etools roster) into `extraOtherSource` (the name matches an entity of that type in
the full 5etools index under a different source) and `extraUnknown` (the name matches no 5etools entity of
that type at all). Both MUST be reported so cross-printed entities are distinguished from likely false
positives.

#### Scenario: Cross-source vs unknown extra are distinguished

- **WHEN** the recall check runs for a book and the canonical contains an entity that exists in 5etools
  under a different source, plus one that matches no 5etools entity at all
- **THEN** the first is reported under `extraOtherSource` and the second under `extraUnknown`

### Requirement: Flag unknown extra entities for review

The system SHALL provide `POST /admin/books/{id}/flag-unknown-entities?type={type}` that marks each
`extraUnknown` entity of the requested type in the book's canonical `NeedsReview = true`, rewriting the
canonical with the flag set. It MUST be gap-only (an entity already `NeedsReview` is left unchanged), MUST
NOT delete any entity, and MUST NOT flag `extraOtherSource` entities.

#### Scenario: Unknown-extra entities are flagged, not deleted

- **WHEN** the flag operation runs for a book with `extraUnknown` entities of the requested type
- **THEN** each `extraUnknown` entity has `NeedsReview` set true, no entity is removed, and
  `extraOtherSource` entities are untouched

### Requirement: DMG is re-extracted fresh and brought to recall parity

The Dungeon Master's Guide canonical SHALL be regenerated by the current extraction pipeline
(`extract-entities?force=true`, applying the allowlist gate, stat-line strip, and recall matcher), then
have recall + backfill + flag-unknown applied for Monster, Spell, MagicItem, and God. After the run, the
corpus validation MUST report zero FAIL-class issues for `dmg14`.

#### Scenario: DMG passes corpus validation after backfill

- **WHEN** DMG is re-extracted, backfilled across the four types, and `POST /admin/canonical/validate` runs
- **THEN** validation reports zero FAIL-class issues attributable to `dmg14.json`
