## REMOVED Requirements

### Requirement: Deterministic 5etools monster backfill

**Reason**: Generalized into the type-generic `fivetools-entity-backfill` capability. Monster backfill is
now one provider on the shared `EntityBackfillService`; the monster-specific route ceases to exist. All
guarantees (gap-only, idempotent, grounded preserved, mapper-projected, `dataSource:"5etools-backfill"`)
carry over unchanged for `type=Monster`.

**Migration**: Use `POST /admin/books/{id}/backfill-entities?type=Monster` and
`GET /admin/books/{id}/entity-recall?type=Monster` in place of the removed `backfill-monsters` and
`monster-recall` routes.
