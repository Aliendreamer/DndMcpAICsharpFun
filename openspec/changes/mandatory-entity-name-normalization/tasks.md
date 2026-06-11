## 1. EntityNameNormalizer (TDD)

- [ ] 1.1 Write failing unit tests for `EntityNameNormalizer.TitleCase`: all-caps→title (`"CIRCLE OF SPORES"`→`"Circle of Spores"`), small words (`of/the/a`), first-word-is-small-word (`"OF MICE AND MEN"`→`"Of Mice and Men"`), apostrophe-S (`"TASHA'S"`→`"Tasha's"`), hyphenated word capitalization, each acronym preserved (`"750 GP ART OBJECTS"`→`"750 GP Art Objects"`, `"QUICK NPCs"`→`"Quick NPCs"`), already-clean passthrough/idempotency
- [ ] 1.2 Create `Features/Ingestion/EntityExtraction/EntityNameNormalizer.cs` with `TitleCase(string)` + acronym allowlist (`NPC, NPCs, PC, PCs, DM, GP, SP, CP, PP, EP, XP, HP, AC, DC, CR, AoE, D&D`); move the small-word/apostrophe logic out of `CanonicalNameNormalizerService`; make tests green

## 2. Mandatory extraction-time hook (TDD)

- [ ] 2.1 Write a failing orchestrator-level test: an all-caps, otherwise-clean candidate (`"BESTIAL SOUL"`, confidence high) yields a canonical entity `name = "Bestial Soul"` with `needsReview = false`; a split-word garbled candidate stays unchanged and flagged
- [ ] 2.2 In `EntityExtractionOrchestrator` at both candidate→entity sites (~lines 203 and 391), apply the gate (all-caps AND `!HasOcrArtifacts(name.ToLowerInvariant())` → `EntityNameNormalizer.TitleCase`), then run `ExtractionNeedsReview.Derive` on the resulting name; make tests green

## 3. Admin normalizer recompute (TDD)

- [ ] 3.1 Update/extend `CanonicalNameNormalizerServiceTests`: all-caps-clean entity with `needsReview: true` becomes title-cased with `needsReview: false`; garbled entity stays unchanged + flagged; clean entity unchanged; idempotency holds
- [ ] 3.2 Update `CanonicalNameNormalizerService.NormalizeAsync` to delegate casing to `EntityNameNormalizer` and set `needsReview = false` in the all-caps-clean branch; make tests green

## 4. Verify build & suite

- [ ] 4.1 `dotnet build` is clean (0 warnings, warnings-as-errors) and `dotnet test` is fully green, including the pre-existing `CanonicalNameNormalizerService` and extraction tests

## 5. Apply to existing data

- [ ] 5.1 `POST /admin/canonical/normalize` against the running stack; confirm the report title-cases the ~900 all-caps names and recomputes flags (dry-run first, then apply)
- [ ] 5.2 Re-ingest DMG (id=1) and Tasha (id=2); verify `dnd_entities` exact count stays 1080, spot-check title-cased names with preserved acronyms (e.g. `Deck of Many Things`, `750 GP Art Objects`), and confirm the `needsReview` count dropped sharply (validation 422 warnings still 0)
- [ ] 5.3 Review the canonical JSON diff and commit the normalized data
