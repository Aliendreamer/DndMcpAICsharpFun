## Context

`mineru-main-parser` is shipped and validated; the parser pipeline (MinerU service → splitter →
`EntityCandidateScanner` → `DeterministicTypeResolver` → 5etools allowlist gate) is stable. This change
is four surgical recall fixes inside that pipeline, each independently testable, plus a re-extraction to
measure. No architecture change.

## Goals / Non-Goals

**Goals:** recover Dwarf + Gnome (→ 9/9 races) and the tractable spell tail (#1 clean headings, #3
page-category, #5 collisions) → ~340+/361 spells, with zero new noise.

**Non-Goals:** #4 prose-merged / special-char spell names (~17, e.g. "Blindness/Deafness", "Disguise
Self") and #2 missing-`Casting Time` anchors (~5, e.g. Feeblemind, Tree Stride) — deferred (diminishing
returns, higher risk of noise). No change to field/content extraction or the 5etools gate semantics.

## Decisions

**1. Clean spell-name headings (splitter, `MinerUPdfConverter`).** Today the level/school strip runs only
on the `TextLevel is null or 0` branch. Extend the heading branch (`TextLevel > 0`): if a heading text
looks like a spell name+level/school (`IsLevelSchoolLine` true *and* it has a name prefix), emit the
**stripped** name (`StripLevelSchool`) instead of the raw heading. Guard so ordinary section headings
(no level/school token) are untouched. Recovers Prestidigitation, Guiding Bolt. *(StripLevelSchool
already cuts at the first digit/school/`Ist`-OCR, so "PRESTIDIGITATIONTransmutation cantrip" → "PRESTIDIGITATION".)*

**2. Prior-type-preferred collision resolution (`DeterministicTypeResolver` / `EntityNameIndex`).** The
ladder's step 1 (name match → `Force(matchedType, canonical)`) is type-blind: "Dwarf" matches a Monster
and forces Monster even though the candidate's primary prior is Race. Change: when matches exist, prefer
a match whose type **equals the candidate's primary prior** (`TypePrior[0]`); only fall through to a
cross-type match when no same-prior match exists. If the only match is cross-type and the prior is a
gated type, defer to content-first rather than forcing the collision. Recovers Dwarf (Race), Darkvision /
Divination (Spell). *(Needs the name index to expose matches-by-type, or a per-type lookup.)*

**3. Race-section fallback (splitter).** Race entries follow `"<RACE>"` (title) → `"<RACE> TRAITS"`. When
the bare title is not tagged (Gnome), anchor on a `"<X> TRAITS"` heading and promote `"<X>"` as a
synthetic race heading — dedup against an already-emitted `"<X>"`. Narrow: only the literal `" TRAITS"`
suffix on a short heading, to avoid spurious promotions.

**4. Page→category misalignment (`TocCategoryMap` / `heading-derived-toc-fallback`).** *Investigation-led:*
`EntityCandidateScanner` skips a candidate when `toc.GetCategory(page)` yields a null/Unknown type. The
vanished spells (Gust of Wind p~250, others to p283) are inside the alphabetical spell descriptions, so
either the TOC's "Spells" page range stops short of the chapter's end or there is a front-matter page
offset (OCR `page_idx+1` vs the TOC's printed-page references). Task 4 diagnoses which, then either
extends the spell range to the chapter end or applies the offset. Acceptance: the previously-vanished
spells resolve to `Spell` and survive scanning.

## Risks / Trade-offs

- **Heading cleanup over-stripping** a real section heading that merely contains a digit → mitigated by
  requiring a spell-shaped level/school token (`IsLevelSchoolLine`), not any digit.
- **Prior-preferred matching regressing a correct cross-type** (a real Monster whose prior was mis-guessed)
  → the stat-block ForceMonster rescue still wins first; change only step 1's match selection, and keep
  the live PHB run as the guard (Monster count must hold at 34).
- **Race fallback false positives** ("X TRAITS" that isn't a race) → restrict to the races chapter pages
  and the exact suffix; verify Monster/Class counts unchanged.
- **TOC offset fix shifting other categories** → re-run validates all category counts, not just spells.

## Migration / Validation

Per-fix unit tests, then the acceptance gate: re-extract PHB through the live service and confirm 9/9
races, ~340+/361 spells, Monster=34 unchanged, zero new noise, declines still clean. Rollback = git
revert. No data migration.
