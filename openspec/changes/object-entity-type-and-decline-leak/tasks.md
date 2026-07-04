## 1. Object type — domain + schema

- [x] 1.1 Add `Object` to `Domain/Entities/EntityType.cs`
- [x] 1.2 Define `ObjectFields` POCO (Monster-subset): `armorClass`, `hitPoints`, damage immunities/resistances/vulnerabilities, condition immunities, optional attack `actions` (name, attack bonus, damage, reach/range), short description
- [x] 1.3 Regenerate canonical schemas via `Tools/SchemaGenerator` (delete `obj/canonical-schemas.stamp` if needed) and confirm `Schemas/canonical/ObjectFields.schema.json` is produced

## 2. Object rendering

- [x] 2.1 (test) Write a failing renderer test: an `Object` renders `canonicalText` stating AC, HP, and attack actions
- [x] 2.2 Implement `ObjectCanonicalTextRenderer` (mirror `MonsterCanonicalTextRenderer`)
- [x] 2.3 Wire the renderer into `EntityCanonicalTextDispatcher`; test passes

## 3. Object extraction routing (non-gated)

- [ ] 3.1 Confirm/keep `Object` OUT of the gated-type set (`DeterministicTypeResolver.GatedTypes`) so it is never declined for lack of a 5etools monster match
- [ ] 3.2 (test) Prior-type routing: an AC/HP-bearing non-creature stat block resolves to `Object`, not `Monster`
- [ ] 3.3 Implement the routing + add extraction prompt guidance in `ExtractionPromptBuilder` (siege weapons / AC-HP non-creatures → `Object`)
- [ ] 3.4 Ensure the extraction union decoder handles `ObjectFields` (discriminated-union-extraction-decoding); round-trip test decode → `Object` entity

## 4. Decline-not-leak fix

- [ ] 4.1 (test) Failing test: an extraction result with empty/meaningless `fields` AND a `none`/ambiguity signal (or reasoning-as-`canonicalText`) is recorded as a failure/decline and NO entity is persisted
- [ ] 4.2 (test) Failing test: a result with at least one meaningful field and no ambiguity signal IS persisted normally
- [ ] 4.3 Implement uncertain-extraction detection in the extract/persist path; route to `errors.json`/`declined.json` instead of writing a shell entity
- [ ] 4.4 (test) The recorded uncertain candidate appears in the errors file and is re-processed by an `errorsOnly` re-extraction

## 5. Validation

- [ ] 5.1 `dotnet build` 0 warnings / 0 errors (warnings-as-errors); full non-persistence test suite green
- [ ] 5.2 Update docs if the entity-type surface is documented (CLAUDE.md entity list); no HTTP endpoint change expected, but sync `.http` + `.insomnia` if any route surface changed
- [ ] 5.3 Data validation on DMG siege candidates: ballista/cannon/ram/cauldron/trebuchet extract as `Object` with populated AC/HP/attack fields; zero empty-shell entities carrying reasoning `canonicalText`; `POST /admin/canonical/validate` clean for the affected file
