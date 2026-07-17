# Tasks — extraction-noise-name-gate (CAPTURE-ONLY; implement in a later session)

> Deferred. Artifacts document the bug + fix direction; tasks below are unchecked for a
> future implementation pass. Evidence: archived `2026-07-17-extraction-authority-ladder`,
> Serena memory `operations/live_referee_validation`.

## 1. Tighten `IsEntityLikeName` (primary fix)

- [ ] 1.1 Add a `StatBlockFieldLabel` `[GeneratedRegex]` to `ExtractionSignatures` that matches a name BEGINNING with a stat-block field label: `Armor Class`, `Hit Points`, `Speed`, `Saving Throws`, `Skills`, `Senses`, `Languages`, `Challenge`, `Damage (Immunities|Resistances|Vulnerabilities)`, `Condition Immunities`, or a bare ability-score line (`STR DEX CON INT WIS CHA` style). Anchor on the leading label (not a substring) so a real creature name with an interior token is not caught.
- [ ] 1.2 Broaden the lair-heading reject in `IsEntityLikeName` to any `"<X> LAIR"` heading (drop the `n.StartsWith("A ")` restriction; keep it anchored to end-with-LAIR).
- [ ] 1.3 Add reject patterns for sidebar headings: names beginning `"Effects of "` and `"Variant:"` / `"Variant "`.
- [ ] 1.4 Wire the new rejects into `IsEntityLikeName` alongside the existing StepHeading/ChallengeFragment/SectionHeading/StructuralHeaders checks; keep the conservative unknown→true default.

## 2. Regression fixture (must run without Docker/Ollama)

- [ ] 2.1 Add a unit-test fixture over `IsEntityLikeName`: known-BAD names must return false — `"Damage Immunities poison"`, `"AN ANARCH's LAIR"`, `"Effects of the Mold"`, `"Telepathic Torment"`, `"Variant: Chromatic Drakes"`, `"Armor Class 14 (natural armor) Hit Points 71 (13d8 + 13) Speed 30 ft."`.
- [ ] 2.2 Known-GOOD names must return true — `"Tortle"`, `"Babau"`, `"Archdruid"`, `"Path of the Battlerager"`, `"Deep Gnome"`, plus a sampling of existing real entities, to prove no recall regression.

## 3. Secondary — scanner root-cause investigation (scope-gate)

- [ ] 3.1 Trace WHERE these fragments become candidate section-headers (`EntityCandidateScanner` + bookmark/MinerU TOC segmentation); document the mechanism in the change.
- [ ] 3.2 Decide (record the decision): is the name-gate backstop sufficient, or is a scanner-segmentation fix warranted? Only widen scope to a scanner change if name-gate patterns prove whack-a-mole.

## 4. Verify

- [ ] 4.1 `dotnet build` clean (warnings-as-errors) + full `dotnet test` green, including the new fixture.
- [ ] 4.2 (Optional, manual) Re-extract a keyless MTF or MPMM copy with the referee off and confirm the listed fragments now land in `.declined.json` while real entities are preserved (see `operations/live_referee_validation` for the keyless-registration technique).
