## Context

The B fallback of the hybrid push. A deterministic completeness pass over an official book's spells, using
5etools as the authoritative source. No LLM, no re-extraction run. The parsed canonical is the source of
truth; backfill only fills verified gaps and marks them distinctly.

## Decisions

**Endpoint + service.** `POST /admin/books/{id}/backfill-spells` → `SpellBackfillService`. The service:
1. Resolves the book's `FivetoolsSourceKey` (via `BookSourceRegistry`); if none (homebrew) → 400/no-op.
2. Loads the canonical `<slug>.json`; collects existing Spell entity normalized names.
3. From 5etools (`FivetoolsRecordIndex` or the spell files filtered by `source == sourceKey`), for each
   spell whose normalized name is NOT in the canonical, build a Spell entity and append.
4. Writes the canonical back (same writer the extraction uses).
Returns a summary `{ backfilled: [names], alreadyPresent: N }`.

**Mapping 5etools spell → Spell entity.** Envelope: `id = EntityIdSlug.For(bookKey, EntityType.Spell,
name)`, `type=Spell`, `name`, `sourceBook`/`edition` from the registry, `page` from the 5etools record,
`firstAppearedIn` = same. `fields`: `entries` = one `{type:"entries", name:"Description", entries:
<5etools.entries>}` block (matching parsed shape), `entriesHigherLevel` = `<5etools.entriesHigherLevel>`,
`damageInflict`/`conditionInflict` copied if present else null. `dataSource="5etools-backfill"`,
`disposition="Accepted"`, `needsReview=false`, `srd`/`srd52` from the 5etools record if available else
false. *(Level/school/casting-time are not stored in the parsed Spell `fields` today, so backfill matches
that shape — a separate enrichment concern, not this change.)*

**Idempotent + non-destructive.** Only append entities whose normalized name is absent. Never modify or
overwrite a parsed/hand-authored entity. Re-running is a no-op once complete.

## Risks / Trade-offs

- **Source divergence** — backfilled entities come from 5etools, not the parsed PDF text. Mitigated by the
  `dataSource:"5etools-backfill"` marker (greppable/auditable) and by only filling verified parser gaps.
- **Id collision** with a parsed entity under a slightly different name → the diff is by normalized name;
  `EntityIdSlug.For` yields the same slug, so a same-norm entity is treated as present (skipped). Verify no
  dup ids after backfill (canonical validation catches duplicate ids).
- **XPHB vs PHB** — filter strictly to `source == the book's PHB key`, not XPHB, so 2014 data is used.

## Validation

Unit tests: mapping a 5etools spell (with entries + entriesHigherLevel + damageInflict) → a well-formed
Spell entity; the service skips already-present spells and appends only gaps; homebrew (no source key) →
no-op. Live: `POST /admin/books/2/backfill-spells` on PHB → 6 backfilled, 355 already present, canonical
validates (no dup ids, no new FAIL), total PHB spells = 361/361.
