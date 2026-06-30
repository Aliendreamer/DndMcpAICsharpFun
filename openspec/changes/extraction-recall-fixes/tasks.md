## 1. Clean spell-name headings (splitter) — DONE (9b9a5bf)

- [x] 1.1 In `MinerUPdfConverter`, extend the `TextLevel > 0` (heading) branch: if the heading text satisfies `IsLevelSchoolLine` AND has a name prefix before the level/school marker, emit `StripLevelSchool(text)` instead of the raw text; otherwise emit unchanged. TDD: "PRESTIDIGITATIONTransmutation cantrip"→"PRESTIDIGITATION"; "GUIDING BOLT Ist-level evocation"→"GUIDING BOLT"; "PART 3 | SPELLS"→unchanged.

## 2. Race-section fallback (splitter) — DONE (9b9a5bf)

- [x] 2.1 In `MinerUPdfConverter`, when a short heading ends with " TRAITS", promote the preceding word(s) as a synthetic race `section_header`, deduped against the last emitted heading norm. TDD: "GNOME TRAITS" (no prior bare "GNOME")→emits "GNOME"; "DWARF TRAITS" after a bare "DWARF"→no duplicate.

## 3. Prior-type-preferred collision resolution (resolver)

- [x] 3.1 Expose per-type lookup from the name index (or matches-by-type) so the resolver can ask "is there a match of type T for this name?". TDD on `EntityNameIndex`.
- [x] 3.2 In `DeterministicTypeResolver`, change step 1: when matches exist, prefer the match whose type == `candidate.TypePrior[0]`; force a cross-type match only if no same-prior match exists; if the only match is cross-type and the prior is gated, defer to content-first. Keep the stat-block ForceMonster rescue ahead of this. TDD every branch: Race "Dwarf" w/ Monster+Race matches→Race; Spell "Darkvision" w/ only non-Spell match→defer; cross-type, non-gated prior→unchanged; stat-block + non-Monster match→Monster.

## 4. (removed) Page→category misalignment — hypothesis disproven

The vanished-spell cause was diagnosed as transient empty Ollama responses, not a TOC bug (Fireball p242
& Wish p289 extracted on the same page band where Gust of Wind p249 failed). The existing 3-attempt
`ExtractionRetryPolicy` records them in `errors.json`; the existing `errorsOnly` retry recovers them
(folded into Task 6). No source change.

## 5. Build, docs

- [x] 5.1 `dotnet build` 0 warnings; full non-persistence suite green.
- [x] 5.2 No `.http`/insomnia change (no endpoint change). Update CLAUDE.md only if behaviour changes (it does not materially).

## 6. Live validation (acceptance gate)

- [x] 6.1 Re-extract PHB through the live `mineru:8000` service (force), then run an `errorsOnly` pass to recover transient empty-response failures.
- [x] 6.2 Confirm: **9/9 races** (Dwarf + Gnome recovered), a higher spell count (cleaned headings + collisions + recovered empties), Monster still 34, classes still 12, **zero new noise**, declines still clean. Record the before/after delta.
