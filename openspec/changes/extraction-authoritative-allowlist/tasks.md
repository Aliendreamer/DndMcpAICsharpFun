## 1. Gated-type set + official determination

- [ ] 1.1 Add a single source of truth for the gated set `{Spell, Monster, Class, Race, Background, Feat, Condition, God}` (e.g. a static `IReadOnlySet<EntityType>` on the resolver or a small `AllowlistGatedTypes` type) (TDD: gated set contains the 8, excludes Item/MagicItem/Plane)
- [ ] 1.2 Confirm the official signal = `IngestionRecord.FivetoolsSourceKey` non-empty; surface it to the resolver call sites (TDD via the resolver/orchestrator: key present → official; absent → homebrew)

## 2. DeterministicTypeResolver: Decline outcome + gate

- [ ] 2.1 Add a `Decline` value to `DeterministicOutcome` and a decline reason carrier on `TypeResolution` (e.g. `string? DeclineReason`); add `Decline(reason)` factory (TDD: shape)
- [ ] 2.2 Extend `Resolve` with an `isOfficial` parameter and insert the gate as ladder step (after the stat-block/magic-item force, before the entity-like drop): `isOfficial && all prior types gated && no match && no stat block → Decline("no_5etools_match")` (TDD: official+{Class}+no-match→Decline; official+stat-block→Force Monster NOT Decline; official+{Class,Item}→Defer; official+empty-prior→Defer; homebrew+{Class}→Defer; matched→Force unchanged)
- [ ] 2.3 Run existing `DeterministicTypeResolverTests` — reconcile any affected by the new ladder step (intended change)

## 3. Orchestrator: collect + write declined records

- [ ] 3.1 Pass `isOfficial` (from `record.FivetoolsSourceKey`) into both `Resolve` call sites (full-run loop + errors-only loop) (TDD via orchestrator harness)
- [ ] 3.2 On a `Decline` outcome, append a declined record `{ id, name, type, reason }` to an in-run list and skip extraction (no LLM call, no entity, not added to `doneIds`/checkpoint as extracted) (TDD: declined candidate → no LLM call, absent from `entities`)
- [ ] 3.3 Write the declined list to `books/canonical/<book-slug>.declined.json` via a new sibling writer (mirror the errors/warnings writer); empty list → write an empty file or skip per the errors/warnings convention (TDD: declined records land in the sibling file, separate from `errors.json`)
- [ ] 3.4 Ensure `errorsOnly` re-extraction does not retry declined candidates (the retry set is built from `errors.json` only) (TDD: a book with a `.declined.json` does not re-extract its declined entries)

## 4. DI / wiring / build

- [ ] 4.1 Register the declined-records writer and any gated-set/options in DI as needed; keep `EntityNameIndex`/`EntityNameMatcher` singletons unchanged
- [ ] 4.2 `dotnet build` 0 warnings (warnings-as-errors); full non-persistence suite green (reconcile intended-change tests)
- [ ] 4.3 Update `DndMcpAICsharpFun.http` / `dnd-mcp-api.insomnia.json` only if an endpoint changed (none expected); add a CLAUDE.md note on the new `<book-slug>.declined.json` sibling

## 5. Live validation — PHB re-run (acceptance gate)

- [ ] 5.1 Rebuild the app image; ensure `5etools/` mounted + qwen3 on GPU; re-extract PHB (`force=true`) on the combined pipeline
- [ ] 5.2 Inspect the first checkpoint + final canonical: Class ~12 (was 397), race stat-block fields (`Ability Score Increase`/`Age`/`Size`/`Speed`/`Languages`) gone, OCR garble gone; `phb14.declined.json` populated with the rejected noise
- [ ] 5.3 Confirm recall + real entities intact: Bard→Class, appendix animals→Monster, real spells present, clean canonical names; spot-check `declined.json` for any wrongly-declined real entity
- [ ] 5.4 Record before/after deltas in the change; ready to archive (after `extraction-name-resolution`) + ingest the corrected canonical
