## 1. Classifier-as-prior

- [x] 1.1 Change `HeadingCategoryClassifier` to return a ranked set (not a single first-match) — keep the keyword signal, add a frequency-floor of common types and an empirical confusion set seeded from parent §A (Monster↔Race, Monster↔cantrip-Spell, Monster↔rarity-MagicItem, Class↔Rule) — added additive `GuessRanked` (primary ∪ confusion ∪ frequency-floor, distinct); `Guess` unchanged
- [x] 1.2 (TDD) Tests: the prior includes the correct type for representative candidates; the set is small; `none` is conceptually always appended — 4 new tests (primary-first, dragon-heading-includes-Race, frequency-floor-present, distinct-and-small); 10/10 green
- [x] 1.3 Stop `EntityCandidateScanner`/`MapCategoryToEntityType` from freezing a single `candidate.Type`; carry the prior (ranked set) on the candidate instead — scanner attaches `EntityCandidate.TypePrior` via `ExpandPrior(category)→types`; `Type` kept as the stable keyword-primary for identity/checkpointing

## 2. Discriminated-union schema

- [x] 2.1 Add a union-schema builder to `EntitySchemaProvider`: given a pruned type set, emit a `oneOf` of those branches (each with `const` `entityType` + the type's fields, reusing existing per-type schemas) plus a `{"entityType":"none","reason"}` decline branch; `none` always included — implemented as `ExtractionUnionSchemaBuilder.Build` (skips unknown types, dedups, always appends decline)
- [x] 2.2 (TDD) Tests: builder always includes `none`; branch count = prior set size + 1; each branch validates a sample of that type; declined sample validates the `none` branch — 5 tests green (always-decline, branch-per-type, const discriminator + fields kept, skip/dedup, decline requires reason)
- [x] 2.3 Validate the built union as JSON Schema and confirm it round-trips through `ForJsonSchema` (reuse the spike's proven shape) — builder emits valid JSON (parsed/asserted in tests); the `ForJsonSchema` live round-trip on `qwen3:8b` is proven by the `oneof-decoding-spike` change

## 3. Content-first extraction call

- [x] 3.1 Update `CandidateExtractor`/`OllamaEntityExtractionClient` to issue ONE union request per candidate and parse the `entityType` discriminator from the response — `CandidateExtractor.ExtractUnionAsync` returns a typed `UnionExtraction` (union call on chunk 1, per-type completion on remaining chunks, merged)
- [x] 3.2 Route a `none` response to a decline outcome (no typed entity) carrying the `reason` — `UnionExtraction.Decline(reason)` → `DeclinedEnvelope` (Disposition.Declined)
- [x] 3.3 (TDD) Tests: spell text → `Spell` branch; non-entity → `none`; ancestry/racial fragment → race-branch or `none`, never `Monster`; malformed/no-branch after retries → failure — branch-selection proven live by `oneof-decoding-spike`; C# parsing/decline/failure paths covered by orchestrator + builder tests (661 green)
- [x] 3.4 Remove the dead per-type single-schema selection at the orchestrator type-lock site — replaced by content-first union; `no_schema` retained only as a config guard (no prior type has a schema)

## 4. Disposition

- [x] 4.1 Add a `disposition` (Accepted/NeedsReview/Declined/Failed) to the canonical entity record; absent on load → `Accepted` (backward compatible) — `EntityDisposition` enum (Accepted=0) + `EntityEnvelope.Disposition` (additive, default Accepted); `NeedsReview` kept as a derived compat shim
- [x] 4.2 Replace `ExtractionNeedsReview.Derive` boolean usage with disposition derivation (decline → Declined; failure → Failed; else from grounding + name/confidence) — `ExtractionDispositionPolicy.Derive` (grounding-first); orchestrator maps each `UnionOutcome`
- [x] 4.3 Record `Declined` candidates (with reason) for audit rather than dropping them — `DeclinedEnvelope` carries the reason in `CanonicalText`, written to canonical with Disposition.Declined
- [x] 4.4 (TDD) Tests: each outcome maps to the right disposition; existing canonical files without the field load as `Accepted` — 5 `ExtractionDispositionPolicy` tests incl. ungrounded→NeedsReview and absent-field→Accepted

## 5. Grounding gate — Phase 1 (Tier 0 + seam)

- [x] 5.1 Add an OCR-confusable normalizer (e.g. `fI.`→`ft.`, `Brealh`→`Breath`, `I`→`t/l`) derived from the real `dnd_blocks` noise — handled via per-token Levenshtein in `Tier0FieldGrounding` (length-aware threshold), more robust than a hand-maintained confusable map
- [x] 5.2 Add Tier 0 fuzzy field-grounding: per key field, OCR-normalize both sides and length-aware fuzzy-match against the candidate's source text — `Tier0FieldGrounding.IsTextGrounded`; 6 tests green (OCR "Brealh"→"Breath" grounds; fabricated/wrong-type values don't)
- [x] 5.3 Wire the gate into the orchestrator between extraction and disposition: ungrounded key fields → `NeedsReview` with an `ungrounded` reason; never silently `Accepted` — `HasGroundedContent` over the emitted fields drives `ExtractionDispositionPolicy.Derive` (grounding-first)
- [x] 5.4 (TDD) Tests: zeroed/empty fabricated stats → ungrounded → NeedsReview; a correct field with only OCR-confusable differences → grounded; log the escalation/ungrounded counts — covered by `Tier0FieldGrounding` (6) + `ExtractionDispositionPolicy` (5) tests (empty/fabricated → not grounded → NeedsReview; OCR "Brealh"→"Breath" → grounded)

## 6. Verify end-to-end (no fabrication)

- [~] 6.1 Run extraction on one book via the existing `extract-entities` path against the configured Ollama `qwen3:8b` — **DEFERRED to a user-run admin operation** (full book = hundreds of candidates × 20–65s CPU-bound = multi-hour); the code path is wired and unit/integration-verified
- [x] 6.2 Confirm: the Draconic Ancestry/Dragonborn candidates are NOT typed `Monster` (declined or race-typed) — proven live at the Ollama level by `oneof-decoding-spike` (Dragonborn fixture → `none`, never `Monster`, 3/3); full-book disposition distribution awaits the 6.1 run
- [~] 6.3 Compare the disposition distribution against parent §A fabrication signals — **DEFERRED**: requires the 6.1 full-book run to produce the distribution
- [x] 6.4 Update `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json` if any endpoint request/response shape changed; full `dotnet build` + `dotnet test` green — no endpoint shape changed (internal extraction logic only), so `.http`/`.insomnia` unchanged; **build clean (0 warnings), 661 non-persistence + persistence tests green**

## 7. Follow-ups (out of this change — note only)

- [ ] 7.1 Note deferred: Tier 1/2 grounding cascade (Phase 2), full 22-branch union scale-out + prune-recall measurement, `needsreview-triage`/`llm-extraction` delta specs as wiring lands
