# Tasks — extraction-noise-name-gate

> Implemented 2026-07-17. Evidence: archived `2026-07-17-extraction-authority-ladder`,
> Serena memory `operations/live_referee_validation`.

## 1. Tighten `IsEntityLikeName` (primary fix)

- [x] 1.1 Added `StatBlockFieldLabel` `[GeneratedRegex]` matching a name BEGINNING with a stat-block field label: `Armor Class` (`armou?r`), `Hit Points`, `Speed`, `Saving Throws`, `Skills`, `Senses`, `Languages`, `Damage (Immunities|Resistances|Vulnerabilities)`, `Condition Immunities`. Anchored `^…\b` on the leading label (not a substring) — verified `"Constrictor Snake"` (Con…) still admits. (`Challenge N` stays covered by the existing `ChallengeFragment`. Bare ability-score lines were NOT added — no such leak in evidence and the abbreviations risk false positives; revisit if one appears.)
- [x] 1.2 Broadened the lair-heading reject: `LairHeading` regex changed from `\blair\b` (gated by `n.StartsWith("A ")`) to end-anchored `\blair\s*$`, and the `StartsWith("A ")` restriction dropped — so `"AN ANARCH's LAIR"` is now rejected while a real name merely containing "lair" mid-string is not.
- [x] 1.3 Added `SidebarHeading` regex `^(effects\s+of\b|variant\b)` for `"Effects of …"` and `"Variant:"`/`"Variant …"` sidebars.
- [x] 1.4 Wired both new rejects into `IsEntityLikeName` alongside the existing checks; conservative unknown→true default preserved. (Gate is applied in `DeterministicTypeResolver.Resolve` line 58.)

## 2. Regression fixture (runs without Docker/Ollama)

- [x] 2.1 Extended `ExtractionSignaturesTests.IsEntityLikeName_rejects_headings_and_fragments` with known-BAD `InlineData` (all now return false): `"Damage Immunities poison"`, `"Armor Class 14 (natural armor) Hit Points 71 (13d8 + 13) Speed 30 ft."`, `"Hit Points 71 (13d8 + 13)"`, `"Saving Throws Dex +5, Con +7"`, `"Condition Immunities charmed, frightened"`, `"AN ANARCH's LAIR"`, `"Effects of the Mold"`, `"Variant: Chromatic Drakes"`. **NOTE:** `"Telepathic Torment"` was deliberately NOT added as a name-gate reject — it is a monster *trait* name, indistinguishable from a real entity by name alone; it is a scanner-level residual (see 3.2), not name-gatable without risking real entities.
- [x] 2.2 Known-GOOD `InlineData` (still return true): `"Tortle"`, `"Babau"`, `"Archdruid"`, `"Deep Gnome"`, `"Path of the Battlerager"`, `"Constrictor Snake"` — proving no recall regression.

## 3. Secondary — scanner root-cause investigation (scope-gate)

- [x] 3.1 Traced: the fragments originate UPSTREAM in the MinerU/bookmark segmentation. `EntityCandidateScanner.Scan` faithfully turns each parser-emitted `section` (`s.Section`/`s.Text`/`s.Page`) into a candidate; stat-block lines / mid-stat-block trait sub-headings that MinerU mis-labels as document headings become spurious candidates. `IsEntityLikeName` (applied in `DeterministicTypeResolver.Resolve`) is the last-line filter that catches them regardless of the parser.
- [x] 3.2 DECISION: **name-gate backstop is sufficient; no scanner/parser fix in this change.** Rationale: (a) the field-label/sidebar/lair leaks — the bulk — are caught cleanly and reliably by the name patterns; (b) the true root (MinerU mis-segmentation) is an EXTERNAL parser, disproportionate to fix here; (c) the only residual the name gate can't catch is trait-name leaks (`"Telepathic Torment"`), which are rare and low-harm (they surface as low-authority `homebrew`, never dropped, and consumers down-weight `homebrew`). Revisit a scanner heuristic (e.g. reject a section whose page maps to a monster stat block AND whose name ≠ the monster's name) ONLY if trait-name leaks prove frequent.

## 4. Verify

- [x] 4.1 `dotnet build` clean (0 warn/0 err, warnings-as-errors) + full `dotnet test` green: **1447/1447** (was 1433; +14 fixture cases).
- [ ] 4.2 (Optional, manual) Re-extract a keyless MTF or MPMM copy with the referee off and confirm the listed fragments now land in `.declined.json` while real entities are preserved (see `operations/live_referee_validation` for the keyless-registration technique). Not run — the CI fixture (2.1/2.2) is the gate; live re-extract is optional confirmation only.
