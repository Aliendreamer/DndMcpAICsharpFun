## Context

Canonical books carry a top-level `tables[]` of `CanonicalTable` (`id, name, columns[], rows[{cells:[{value, provenance}]}]`), currently populated by MinerU table extraction (`mineru-table-extraction`, archived). A corpus-wide scan of all 1325 tables found 45% degenerate, dominated by **column-collapse** (307 single-column + 35 ragged-merged tables that are real class/spell/wild-magic/armor tables the parser flattened), plus 176 stat-block lines and 76 prose-sidebars. Filtering (`filter-degenerate-tables`, deferred) would delete real, high-value tables.

For every official book we hold, 5etools already has the same tables as clean structured JSON. `read_path_frontier` established the pattern: for official content, 5etools is the deterministic structured source — prefer projecting from it over rescuing OCR output (same lever that made `spell-class-join` and `fivetools-field-fill` trivial). Downstream is already built: `StructuredFactProjector` consumes `CanonicalTable` into Postgres `StructuredTables`/`Rows`, and `CharacterResolutionService` resolves against them (e.g. `ResolveBreathWeaponAsync` → the Dragonborn Draconic Ancestry table) — but only if the table carries the id the resolver expects (`phb14.table.draconic-ancestry`). MinerU emitted `Table 7`, so resolution never wired up.

## Goals / Non-Goals

**Goals:**
- Replace official-book canonical `tables[]` with clean, correctly-id'd tables projected deterministically from 5etools.
- Cover both captioned embedded `{type:table}` blocks and class-progression tables (synthesized for martials, parsed for casters).
- Wire the projected tables into the existing resolution engine with zero engine changes.
- Keep it build-time canonical authoring (a `Tools/` console), reviewable in the canonical diff, hand-correctable.

**Non-Goals:**
- No MinerU homebrew junk-filter in this change (`filter-degenerate-tables` stays deferred until a non-5etools book is added and can be live-validated).
- No `table-name-from-heading` (moot for official books once tables come from 5etools).
- No new runtime HTTP endpoint or MCP tool; no changes to `StructuredFactProjector` / `CharacterResolutionService`.
- No projection for homebrew/non-5etools books (console skips them).

## Decisions

**D1 — A `Tools/ProjectTables` console, not an endpoint.** Table projection is deterministic canonical authoring (like a code generator), not runtime product behavior; per dev-flow, one-time/repeatable data transforms are `Tools/` consoles reusing the app's services directly (`new CanonicalJsonLoader(...)`, no DI/DB). Re-runnable per book (`ProjectTables <slug>` or `--all`). Alternative rejected: a `POST /admin/books/{id}/project-tables` endpoint (mirrors `backfill-spells`) — adds an auth/HTTP surface for something no product flow calls at runtime.

**D2 — Replace `tables[]` wholesale, don't merge.** For an official book the console discards MinerU tables and writes only the 5etools projection. Merging would re-introduce the column-collapsed duplicates the change exists to remove. Only books with a `fivetoolsSourceKey` (resolved via `BookCatalog`) are processed; others are skipped with a message.

**D3 — Captioned embedded tables → id from caption.** Recurse the book's 5etools entity files (races, classes, feats, backgrounds, items, optionalfeatures) for entries with `source == KEY`; project each `{type:table}` that has a `caption`. `id = <book-slug>.table.<EntityIdSlug(caption)>`. Reuse `EntityIdSlug` so ids match a hypothetical re-extract (no second slug impl to drift). Uncaptioned embedded tables are skipped (no stable id, low value).

**D4 — Class progression tables synthesized to one wide table per class.** Mirror how the PHB prints it — a single table keyed by level 1–20:
- *Martial* (`classTableGroups` absent): columns `Level | Proficiency Bonus | Features`. Proficiency bonus from the standard 5e curve (`2 + (level-1)/4`); Features per level gathered from `classFeatures` gained at that level.
- *Caster* (`classTableGroups` present): append the group columns — strip `{@filter label|...}`/`{@tag ...}` markup to the plain label, expand `rowsSpellProgression` (compact per-level slot arrays) into per-level slot cells, join on level.
- `id = <book-slug>.table.<class-slug>` (e.g. `phb14.table.wizard`). Alternative rejected: separate tables per `classTableGroup` — fragments the one printed table and complicates ids/naming for no consumer benefit.

**D5 — Unique-id invariant + load round-trip.** `CanonicalJsonLoader` throws on duplicate ids. Enforce `ids.Should().OnlyHaveUniqueItems()` on the final `tables[]`; on a slug collision append `-2`, `-3`. After writing, reload the file through `CanonicalJsonLoader` to prove it is ingestable. Tag every projected table `dataSource:"5etools-table-projection"` for audit.

**D6 — Provenance.** Projected cells carry provenance pointing at the 5etools source (`sourceBook = KEY`, `page` from the entity where available), distinct from MinerU `blockId` provenance, so the origin is auditable in the canonical.

## Risks / Trade-offs

- **Martial-table synthesis diverges from the printed book** (missed/extra feature at a level, wrong prof curve) → unit-test Fighter against the known PHB table; the standard prof curve is fixed and small.
- **Caster markup/`rowsSpellProgression` parsing is fiddly** (5etools tag variants, half/third-caster progressions) → unit-test Wizard (full caster) explicitly; expand only the encodings present in our official corpus, skip-and-log any unrecognized group rather than emit a wrong table.
- **Wholesale replacement discards MinerU tables that had no 5etools equivalent** → acceptable: for official books 5etools is authoritative and complete; any genuinely book-only table is out of scope here and recoverable from git if a gap surfaces.
- **id mismatch with what the resolver expects** → the live gate ingests PHB and exercises the real resolution path, not just unit projection, to confirm `phb14.table.draconic-ancestry` resolves.
- **SRD slug** (`system-reference-document`) may not map to a 5etools source key cleanly → console skips any book whose `fivetoolsSourceKey`/`BookCatalog` entry is absent; SRD handled only if a key resolves.

## Migration Plan

1. Land the console + projectors (unit-green).
2. Run `ProjectTables --all` to rewrite official canonicals; review the `tables[]` diff per book.
3. Live-validate on PHB (inspect `phb14.json`; ingest-entities; exercise the resolution path).
4. Rollback = `git checkout books/canonical/*.json` (MinerU tables restored) — no schema/DB migration involved.

## Open Questions

- None blocking. Half-/third-caster `rowsSpellProgression` handling is scoped to whatever caster/half-caster classes appear in the official corpus; unrecognized encodings are skipped-and-logged, deferred to a follow-up if any surface.

## Addendum (live-validation-driven scope expansion)

**Finding:** projecting the generic 5etools Draconic Ancestry table (columns `Dragon|Damage Type|Breath Weapon`, area+save merged) achieves id alignment but does NOT drive `CharacterResolutionService` breath-weapon resolution, which needs a NORMALIZED table (`ancestry`/`damageType`/`breathArea`/`saveAbility`), a companion `phb14.choiceset.draconic-ancestry`, and a `phb14.table.breath-damage-by-tier` — and the real PHB canonical had 0 choiceSets. Generic reference-table projection and resolution-engine tables are two concerns sharing an id namespace.

**D7 — resolution-owned id namespace.** A dedicated `DraconicAncestryResolutionProjector` emits the normalized draconic table + choiceset + tier table for PHB; the generic captioned projection CEDES the `phb14.table.draconic-ancestry` id (excluded from the generic output) so the two shapes never collide. The console composes: generic tables (minus resolution-owned ids) + resolution tables, and resolution choiceSets. Breath-cell parse: `"<area> (<Abbr>. save)"` → `breathArea=<area>`, `saveAbility=<Dexterity|Constitution|…>`; `damageType` = Damage Type cell lowercased; `ancestry` = Dragon cell. Tier table from `BreathWeaponRules` (1→1d10, 2→2d6, 3→3d6, 4→4d6). Validated by a real-Postgres (Testcontainers) integration test through `StructuredFactProjector` → `CharacterResolutionService`, plus a real-corpus check that the projected `phb14.json` contains the artifacts.
