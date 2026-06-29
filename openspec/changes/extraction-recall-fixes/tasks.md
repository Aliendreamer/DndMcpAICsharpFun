## 1. Clean spell-name headings (splitter)

- [ ] 1.1 In `MinerUPdfConverter`, extend the `TextLevel > 0` (heading) branch: if the heading text satisfies `IsLevelSchoolLine` AND has a name prefix before the level/school marker, emit `StripLevelSchool(text)` instead of the raw text; otherwise emit unchanged. TDD: "PRESTIDIGITATIONTransmutation cantrip"→"PRESTIDIGITATION"; "GUIDING BOLT Ist-level evocation"→"GUIDING BOLT"; "PART 3 | SPELLS"→unchanged. Keep the existing non-heading splitter branch + tests green.

## 2. Race-section fallback (splitter)

- [ ] 2.1 In `MinerUPdfConverter`, when a short heading ends with " TRAITS", promote the preceding word(s) as a synthetic race `section_header`, deduped against the last emitted heading norm. TDD: "GNOME TRAITS" (no prior bare "GNOME")→emits "GNOME"; "DWARF TRAITS" after a bare "DWARF"→no duplicate. Guard the suffix match so non-race "… TRAITS" prose is not promoted (heading-tagged + short only).

## 3. Prior-type-preferred collision resolution (resolver)

- [ ] 3.1 Expose per-type lookup from the name index (or matches-by-type) so the resolver can ask "is there a match of type T for this name?". TDD on `EntityNameIndex`.
- [ ] 3.2 In `DeterministicTypeResolver`, change step 1: when matches exist, prefer the match whose type == `candidate.TypePrior[0]`; force a cross-type match only if no same-prior match exists; if the only match is cross-type and the prior is gated, defer to content-first. Keep the stat-block ForceMonster rescue ahead of this. TDD every branch: Race "Dwarf" w/ Monster+Race matches→Race; Spell "Darkvision" w/ only non-Spell match→defer; cross-type, non-gated prior→unchanged; stat-block + non-Monster match→Monster.

## 4. Page→category misalignment (investigate + fix)

- [ ] 4.1 Diagnose: for the vanished spells (e.g. Gust of Wind), compare the promoted candidate's page to `TocCategoryMap`'s Spell range and to the TOC's printed-page references — determine whether it is a range-stops-short or a front-matter page-offset issue. Record the finding in the design.
- [ ] 4.2 Fix the mapping accordingly (extend the spell chapter's page span to its end, or apply the offset) in the TOC/heading-derived-toc-fallback path. TDD: an in-chapter spell page → Spell; an out-of-scope page unchanged.

## 5. Build, docs

- [ ] 5.1 `dotnet build` 0 warnings; full non-persistence suite green.
- [ ] 5.2 No `.http`/insomnia change (no endpoint change). Update CLAUDE.md only if behaviour described there changes (it does not materially).

## 6. Live validation (acceptance gate)

- [ ] 6.1 Re-extract PHB through the live `mineru:8000` service (force).
- [ ] 6.2 Confirm: **9/9 races** (Dwarf + Gnome recovered), **~340+/361 spells**, Monster still 34, classes still 12, **zero new noise**, declines still clean. Record the before/after delta.
