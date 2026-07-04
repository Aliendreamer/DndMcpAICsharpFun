## REMOVED Requirements

### Requirement: Missing spells are backfilled from 5etools for official books

**Reason**: Generalized into the type-generic `fivetools-entity-backfill` capability. Spell backfill is now
one provider on the shared `EntityBackfillService`; the spell-specific route ceases to exist. All
guarantees (gap-only append, `dataSource:"5etools-backfill"`, mapper-projected content fields, homebrew
no-op) carry over unchanged for `type=Spell`.

**Migration**: Use `POST /admin/books/{id}/backfill-entities?type=Spell` in place of the removed
`backfill-spells` route.
