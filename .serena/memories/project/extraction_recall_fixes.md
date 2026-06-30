# extraction-recall-fixes — SHIPPED + VALIDATED (2026-06-30)

Follow-on to `mem:parser_upgrade_mineru`. Recovered the PHB tail (Dwarf/Gnome races + spells). Live-validated through the `mineru:8000` service + `errorsOnly`.

## Final PHB numbers (vs mineru-main-parser baseline)
| | base | now |
|---|---|---|
| races/9 | 7 | **9** |
| PHB spells/361 | 323 | **329** (+6) |
| classes/12 | 12 | 12 |
| Monster | 34 | **30** |
| errors | 4 | **0** |
| noise | none | none |
Validation failures: 0 (422 = by-design NeedsReview=5). Total 452 entities.

## What the fixes did (commits on main)
- **`9b9a5bf`** splitter: clean spell-name HEADINGS (`PRESTIDIGITATIONTransmutation cantrip`→`PRESTIDIGITATION`) → recovered Prestidigitation, Guiding Bolt. + the original race `TRAITS` fallback.
- **`eb49493`** + **`8b51ed0`** resolver (`DeterministicTypeResolver` + `EntityNameIndex.MatchOfType`): prefer a same-PRIMARY-prior 5etools match over a cross-type collision. → **Dwarf→Race** (was Monster), **Dragonborn→Race held**, and BONUS **Acolyte/Noble/Sage/Soldier→Background** (were mis-typed Monster — so Monster 34→30 is a CORRECTION, not a regression). NOTE: `8b51ed0` dropped the gated-cross-type DEFER branch that `eb49493` added — it regressed Dragonborn (Monster-prior + only Race match → deferred → mistyped). Corrected logic = same-prior preference, else force the best match (old behaviour).
- **`435dc5c`** Gnome `TRAITS`-rename — see below (didn't land).
- `errorsOnly` pass recovered Command, Gust of Wind, Vicious Mockery (transient empty Ollama responses) + Pseudodragon.
- **Gnome HAND-AUTHORED** in `phb14.json` (`dataSource:"hand-authored"`, id `phb14.race.gnome`) — the code fallback works at the converter but the candidate vanished downstream (see below); hand-authoring is the canonical escape hatch.

## IMPORTANT open bug — downstream silent candidate-drop
The Gnome `GNOME` heading + traits body are emitted CORRECTLY by the converter (confirmed in the conversion cache), with the correct page (36, in the Race range — pages 39-42 around it extract fine). Yet the candidate **vanishes with ZERO trace** — not extracted, not declined, not errored, not logged. Some path in `EntityCandidateScanner`/orchestrator silently drops a valid candidate. **Could be quietly eating other entities too.** Worth a real investigation (not chased — hand-authored Gnome instead). The page→category (`toc.GetCategory`) was ruled out.

## Roadmap (still deferred)
- ~17 prose-merged / special-char spell names (Blindness/Deafness, Disguise Self) + missing-Casting-Time anchors (Feeblemind, Tree Stride). Hard, low-yield.
- The silent candidate-drop (above).
- DEV-FLOW levers (discussed, not built): `think:false` on the Ollama extraction call (~4-5× faster, qwen3 thinking is on) + a page-range/chapter-scoped extract for fast spell validation. Plus: the conversion disk cache is PDF-hash-keyed, so converter-logic changes need a manual `rm books/conversion-cache/*.mineru.json`.
