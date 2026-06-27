## 1. ExtractionSignatures utility (TDD)

- [ ] 1.1 Write `ExtractionSignaturesTests`: token primitives (HasArmorClass/HasHitPoints/HasChallenge/IsSizeTypeLine), `IsCompleteStatBlock` (AC+HP+Challenge true; AC+HP-only false; empty/null false), `IsMagicItem` (rarity / "requires attunement" / "wondrous item" true; plain item false; spell false), `IsEntityLikeName` (real names true; "ACTIONS"/"Appendix D"/"Step 2. Basic Statistics"/"Challenge 7 (2,900 XP)" false)
- [ ] 1.2 Create `Features/Ingestion/EntityExtraction/ExtractionSignatures.cs` (pure static, OCR-tolerant case-insensitive Contains) until 1.1 passes
- [ ] 1.3 Fold `StatBlockSignature.IsCompleteStatBlock` into `ExtractionSignatures`; delete `StatBlockSignature.cs`; update the override caller

## 2. Consolidate existing detectors onto the utility

- [ ] 2.1 `ExtractionCandidateDeduplicator.HasStatBlock` → call `ExtractionSignatures` (behaviour-preserving)
- [ ] 2.2 `StatBlockScanner` token checks → route "Armor Class" matching through `ExtractionSignatures` primitives (keep size/type-line + span logic local)
- [ ] 2.3 Grep the extraction feature: assert no production type other than `ExtractionSignatures` matches the literal "Armor Class"/"Hit Points"/"Challenge" strings

## 3. Deterministic type resolution ladder (TDD)

- [ ] 3.1 Write tests for `ResolveDeterministicType(candidate)`: non-entity-name → Drop; complete stat block + creature-like name → force Monster; stat-block-like text + non-creature name ("Step 2…") → NOT Monster (Drop/Defer); magic-item signature → force MagicItem; ordinary entity (spell/class) → Defer
- [ ] 3.2 Implement the resolver (Drop / forced EntityType / Defer) returning the small result type; ladder order drop → Monster(+guard) → MagicItem → defer
- [ ] 3.3 Rewrite `ExtractOneAsync` to call the resolver: Drop → skip (no error, no LLM call); forced → extract with that type's schema via `BuildTypedEnvelope` (no decline); Defer → existing union path. Delete the inline Monster-override block
- [ ] 3.4 Wire candidate dropping: dropped candidates are skipped before the extraction loop / not counted as errors

## 4. Verify behaviour-preserving + new behaviours

- [ ] 4.1 `dotnet build` clean (warnings-as-errors); run the full non-persistence suite — existing 676 stay green plus the new tests
- [ ] 4.2 Confirm the three intended behaviour changes are covered by tests (guard, drop, MagicItem) and that no consolidation test changed output unexpectedly

## 5. Live validation re-run (MM + PHB + DMG)

- [ ] 5.1 Rebuild the app image; recreate the app on the shared infra; ensure qwen3 on GPU
- [ ] 5.2 Re-extract MM, PHB, DMG (force) and compare to the prior run: tutorial-fragment Monsters gone, Vorpal Sword → MagicItem, no real entity lost vs the known-good lists, override still catches real stat blocks
- [ ] 5.3 Record the before/after distribution deltas in the change before archiving
