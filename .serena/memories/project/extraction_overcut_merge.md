# extraction-overcut-and-merge — SHIPPED + VALIDATED (2026-06-30)

Bucket A+B of the recall tail (follow-on to `mem:project_extraction_recall_fixes`). Both bugs fixed, live-validated on PHB, archived.

## Two bugs fixed
- **Bug A — splitter over-cut** (`MinerUPdfConverter.StripLevelSchool`, commit `b67ff78`): cut the spell name at the FIRST school word, but the name can contain one (`MINOR ILLUSION Illusion cantrip` → `MINOR`). Fix: strip the trailing level/school SUFFIX (digit-cut for leveled; strip `"<school> cantrip"` for cantrips). → recovered **Minor Illusion, Programmed Illusion**; eliminated the `MINOR`/`PROGRAMMED` over-cut junk.
- **Bug B — scanner cross-book merge** (`EntityCandidateScanner.Scan`, commit `1938f17`): `GroupBy(SectionTitle)` merged same-titled sections across the WHOLE book, keyed on `Min(Page)` — a name reused in two chapters fused into one wrong-chapter candidate. Fix: page-proximity merge guard (`MaxPageGap = 3`) — same title >3 pages apart → separate candidates. → recovered **Darkvision** (spell p230 was fusing with the Darkvision invocation heading p184).

## Validated (live PHB via mineru:8000 + errorsOnly)
456 entities | **PHB spells 333/361 (+4 vs prior 329)** | **races 9/9** (Gnome hand-authored) | classes 12/12 | Monster 30 | 0 errors | no noise. All 3 targets recovered, over-cut junk gone. Committed `5141b60`, archived `a5dc9be`.
Cumulative across both recall specs: races 7→9, PHB spells 323→333.

## GOTCHA learned (now in dev-flow SKILL)
`extract-entities?force=true` OVERWRITES the whole canonical → hand-authored entities (Gnome) are LOST; re-apply after every force run.

## Bucket C — the remaining residual vanish (NEXT change, brainstorm started)
Still-missing PHB spells that are NOT A/B:
- **Shield of Faith** — splitter anchor didn't fire (no `SHIELD OF FAITH` header in cache); the level/school line and `Casting Time:` likely not adjacent (column/page break) → cause #2 (missing-anchor) family.
- **Mordenkainen's Sword + Mordenkainen's Private Sanctum** — clean SINGLE header in cache, NOT declined, NOT errored → vanish downstream untraced; apostrophe/normalization or a scanner/dedup edge — UNPINNED.
- (Gnome was this family too; hand-authored.)
Plus the deferred roadmap buckets: ~21 prose-merged/special-char names, ~5 OCR-dropped Casting-Time anchors.
The downstream silent-drop diagnosis tooling: read `EntityCandidateScanner` + `ExtractionCandidateDeduplicator` + the orchestrator candidate loop; trace via cache headers + declined + logs (a valid candidate with NO trace anywhere = the silent drop).
