## ADDED Requirements

### Requirement: Declined by record

The overrides file SHALL hold a `declines` list of `{entityId, reason, at}`. Applying overrides SHALL give an entity
whose id is declined disposition `Declined` and `needsReview: false`, regardless of its checks or approval. A declined
entity MUST NOT be ingested into `dnd_entities` and MUST NOT be listed as flagged on the review page; it stays in the
canonical file. Declines SHALL survive re-runs, follow override id-migration, and be listed as orphans when their entity
is gone.

#### Scenario: Junk is declined

- **WHEN** `mm14.overrides.json` declines `mm14.lore.art-credit` and the book is extracted again
- **THEN** that entity is in `mm14.json` with disposition `Declined` and `needsReview: false`

#### Scenario: Decline outranks failing checks

- **WHEN** a declined entity has failed checks and no approval
- **THEN** its disposition is `Declined`, not `NeedsReview`

#### Scenario: File without declines

- **WHEN** an overrides file has no `declines` key
- **THEN** it loads with no declines and every entity's disposition is derived as before

#### Scenario: Review edit keeps declines

- **WHEN** the admin approves another entity of the same book on the review page
- **THEN** the saved overrides file still holds every decline

### Requirement: Decline from the review page

The `/review` page SHALL offer a Decline action for a flagged entity. It SHALL require a reason, record a decline for the
entity in the overrides file (replacing any earlier decline of the same id), give the entity disposition `Declined`, and
remove it from `dnd_entities`. Overrides, approvals and other declines of the book MUST be kept.

#### Scenario: Admin declines junk

- **WHEN** the admin declines a flagged entity with reason "art caption"
- **THEN** the overrides file holds a decline for it with that reason, its disposition is `Declined`, its `dnd_entities`
  point is deleted, and it is no longer listed as flagged

#### Scenario: Reason required

- **WHEN** the admin declines with an empty reason
- **THEN** nothing is saved and the page shows the reason error
