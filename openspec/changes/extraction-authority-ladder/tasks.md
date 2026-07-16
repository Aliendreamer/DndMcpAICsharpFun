> Implementation is GATED on the MTF (`mtf.json`) review — confirm the decline pattern there first. Tiers ship independently: T1 alone fixes most of SCAG; T2 next; T3 (web, toggled off) last.

## 1. Tier 1 — Index the 5etools subclass roster

- [x] 1.1 In `EntityNameIndex`, add a load of the `subclass[]` array from `5etools/class/class-*.json` (index each subclass `name` AND `shortName` → `EntityType.Subclass`), keeping the base-class `class[]` load ordered first so a base name still wins.
- [x] 1.2 Unit tests: "Path of the Battlerager" matches → `Force(Subclass)`; bare "Mastermind" matches on `shortName`; "Barbarian" still resolves to `Class`; a base-name/subclass-name collision resolves to the base class.
- [x] 1.3 Confirm the end-to-end path: a matched subclass candidate flows through `DeterministicTypeResolver` `Force(Subclass)` → `FivetoolsSubclassMapper` → a grounded `Subclass` entity with the 5etools canonical name/slug (extend an existing extraction test).

## 2. Tier 2 — Book-derived `IsRealEntity` predicate (official + keyless)

- [x] 2.1 Add a subclass-feature-progression signature to `ExtractionSignatures` (≥2 level-gated grants — match "Starting at Nth level", "At Nth level", "Nth-level" patterns), plus a `HasSubstantialProseBody` helper (min length, not mostly-tabular/fragment). Reuse the existing stat-block / spell / magic-item signatures.
- [x] 2.2 Add `IsRealEntity(candidate)` = structural signature OR (`IsEntityLikeName` AND substantial non-tabular body) to `ExtractionSignatures`/`DeterministicTypeResolver`.
- [x] 2.3 In `DeterministicTypeResolver.Resolve`, replace the official-only `no_5etools_match` decline with a predicate gate applied to BOTH official and keyless gated-prior no-match candidates: `IsRealEntity` → `Defer`; else → `Decline`. (Keyless previously fell through unconditionally — this adds its noise filter.)
- [x] 2.4 Unit tests: subclass-feature-progression → `Defer`; prose Background/Feat (entity-like name + body) → `Defer`; "Ability Score Increase" / "d6 Resource" / "CONTENTS" → `Decline`; empty "Barbarian" base-class shell → `Decline`/drop; a 5etools match still `Force`s; a keyless real entity `Defer`s while keyless noise `Decline`s.
- [ ] 2.5 (Tier 3, not done here) Label a deferred official no-match entity `canon-unindexed` (feeds group 3). The regression half — ungrounded fields on an admitted candidate still rejected by the grounding cascade — is confirmed: the cascade itself is untouched and the full suite (1426/1426) passes unchanged.

## 3. Tier 3 — Web authority referee + authority labels

- [ ] 3.1 Add the authority label to the entity envelope + `dnd_entities` payload: `canon` / `canon-unindexed` / `verified-thirdparty` / `homebrew` (additive; existing `dataSource` values unaffected).
- [ ] 3.2 Assign the label deterministically during extraction: 5etools match → `canon`; official no-match (admitted) → `canon-unindexed`.
- [ ] 3.3 Add `IWebAuthorityReferee` over the existing SearXNG client — refute-biased (confirm only on a strong authoritative hit), per-call timeout, by-normalized-name cache, and an off-by-default config toggle.
- [ ] 3.4 Keyless / sourceless path: run the referee → `verified-thirdparty` on a confirming hit, `homebrew` on a miss; NEVER drop the entity.
- [ ] 3.5 Retrieval: surface the authority label on `dnd_entities` results so consumers can filter/down-weight (decide hard-filter vs down-weight for `homebrew`).
- [ ] 3.6 Config keys (toggle, timeout, refute threshold); update `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json` if any admin/retrieval surface changes.
- [ ] 3.7 Unit tests: referee confirm → `verified-thirdparty`; referee miss → `homebrew` (kept); toggle off → no web calls; label persisted to payload; keyless entity never dropped.

## 4. Verify

- [ ] 4.1 `dotnet build` clean (warnings-as-errors) + full `dotnet test` green (mind the known-flaky `CombatServiceEndTests` — passes in isolation).
- [ ] 4.2 Live (after T1, +T2): `POST /admin/books/6/extract-entities?force=true` (SCAG) → SCAG subclasses now ground as `canon`/`canon-unindexed` (Battlerager, Bladesinging, Storm Sorcery, Mastermind…), and no-signature noise ("Ability Score Increase") stays in `.declined.json`. Diff `scag.declined.json` before/after to prove the recovery + preserved noise-filtering.
- [ ] 4.3 Live (T3, after refute-bias tuning): enable the referee on a keyless book (e.g. EEPC once ingested) and spot-check that entities get `verified-thirdparty`/`homebrew` labels and none are dropped.
