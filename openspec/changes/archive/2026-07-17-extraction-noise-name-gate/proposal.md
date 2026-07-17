## Why

T3 live validation (keyless MTF/MPMM extraction, archived change `2026-07-17-extraction-authority-ladder`) surfaced an upstream noise leak: stat-block fragments and sidebar headings are admitted as `Monster` entities instead of being declined. Concrete leaks from `mpmm-keyless`: `"Damage Immunities poison"`, `"AN ANARCH's LAIR"`, `"Effects of the Mold"`, `"Telepathic Torment"`, `"Variant: Chromatic Drakes"`, and a raw stat-line `"Armor Class 14 (natural armor) Hit Points 71 (13d8 + 13) Speed 30 ft."`. These are not entities. This is SEPARATE from the T3 web referee (which correctly refused to confirm them → low-authority `homebrew`, never dropped); the gap is in the upstream name gate. Capturing now for a later implementation session.

## What Changes

- Tighten `ExtractionSignatures.IsEntityLikeName` (the last-line name filter) so stat-block field-label names and sidebar/section headings are rejected before extraction, and land in `.declined.json` instead of being emitted as entities.
- Investigate the deeper cause: the candidate scanner (bookmark + MinerU segmentation) emitting these stat-block lines / mid-stat-block sub-sections as candidate section-headers in the first place.
- Add a regression fixture of the known-bad names so the leak cannot silently return, while asserting real entities (Tortle, Babau, Archdruid, real subclasses/monsters) are preserved.
- No web-referee, retrieval, or endpoint changes.

## Capabilities

### New Capabilities

- `entity-name-noise-gate`: the deterministic name filter that decides whether a scanned candidate NAME denotes a real entity vs. a stat-block fragment / section heading, so non-entity candidates are declined before any extraction LLM call.

### Modified Capabilities

<!-- none: this documents/repairs the name-gate behavior; existing spec requirements are unchanged -->

## Impact

- `Features/Ingestion/EntityExtraction/ExtractionSignatures.cs` — `IsEntityLikeName` + new reject regexes (stat-block field labels, broadened lair heading, "Effects of …", "Variant: …").
- `Features/Ingestion/EntityExtraction/EntityCandidateScanner.cs` (and bookmark/MinerU segmentation) — secondary root-cause investigation.
- `DndMcpAICsharpFun.Tests/…/ExtractionSignatures*` — regression fixture of known-bad + known-good names.
- Evidence: archived change `2026-07-17-extraction-authority-ladder`, Serena memory `operations/live_referee_validation`.
