> Implementation is GATED on the MTF (`mtf.json`) review — confirm the decline pattern there first. Tiers ship independently: T1 alone fixes most of SCAG; T2 next; T3 (web, toggled off) last.

## 1. Tier 1 — Index the 5etools subclass roster

- [ ] 1.1 In `EntityNameIndex`, add a load of the `subclass[]` array from `5etools/class/class-*.json` (index each subclass `name` AND `shortName` → `EntityType.Subclass`), keeping the base-class `class[]` load ordered first so a base name still wins.
- [ ] 1.2 Unit tests: "Path of the Battlerager" matches → `Force(Subclass)`; bare "Mastermind" matches on `shortName`; "Barbarian" still resolves to `Class`; a base-name/subclass-name collision resolves to the base class.
- [ ] 1.3 Confirm the end-to-end path: a matched subclass candidate flows through `DeterministicTypeResolver` `Force(Subclass)` → `FivetoolsSubclassMapper` → a grounded `Subclass` entity with the 5etools canonical name/slug (extend an existing extraction test).

## 2. Tier 2 — Book-derived entity-signature gate (relax the official decline)

- [ ] 2.1 Add a subclass-feature-progression signature (and reuse existing stat-block/spell/item signatures) to `ExtractionSignatures` — a deterministic "is this a real entity vs a bare rules sub-heading" test from the candidate's structure/prose.
- [ ] 2.2 In `DeterministicTypeResolver.Resolve`, change the official-book decline: decline `no_5etools_match` ONLY when the candidate has NO entity signature; when it has a signature, `Defer` to content-first extraction instead.
- [ ] 2.3 Unit tests: signature + no-match official candidate → `Defer` (not `Decline`); no-signature "Ability Score Increase"-class noise → still `Decline`; a 5etools match still `Force`s; homebrew still falls through.
- [ ] 2.4 Label a deferred official no-match entity `canon-unindexed` (feeds Tier-3 labeling in group 3); ensure ungrounded fields on an admitted candidate are still rejected by the grounding cascade (regression).

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
