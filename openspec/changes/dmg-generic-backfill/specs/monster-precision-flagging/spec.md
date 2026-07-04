## REMOVED Requirements

### Requirement: Categorize recall-check extra monsters

**Reason**: Generalized into the type-generic `fivetools-entity-backfill` capability, whose recall check
splits `extra` into `extraOtherSource` / `extraUnknown` for every supported type (Monster included) rather
than monsters only.

**Migration**: Use `GET /admin/books/{id}/entity-recall?type=Monster`; the response carries the same
`extraOtherSource` / `extraUnknown` split.

### Requirement: Flag unknown extra monsters for review

**Reason**: Generalized into the type-generic `fivetools-entity-backfill` capability. The flag operation is
now type-parameterized; the monster-specific route ceases to exist. Guarantees (gap-only, never deletes,
never flags `extraOtherSource`) carry over unchanged for `type=Monster`.

**Migration**: Use `POST /admin/books/{id}/flag-unknown-entities?type=Monster` in place of the removed
`flag-unknown-monsters` route.
