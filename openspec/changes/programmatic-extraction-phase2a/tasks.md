## 1. Truth and fixtures

- [ ] 1.1 Scorer: 5etools truth for Class, Subclass, Race, Subrace, Feat, Background (`class/`, `races.json`, `feats.json`, `backgrounds.json`), per-field agreement and found/expected; `scoreall.sh` and the wrong-and-accepted audit cover the six types
- [ ] 1.2 Commit OCR fixture pages: PHB class pages (barbarian, fighter, wizard incl. tables), PHB race (dwarf), PHB feats, PHB background (acolyte), XGE and TCE subclass pages, one MPMM race, one supplement background (SCC or GGR); update `PAGES.md`

## 2. Table reader

- [ ] 2.1 `tables.py`: rows by y-overlap, columns by header x-spans, per-cell digit repair; tests on the PHB fighter and wizard tables

## 3. Parsers and checks

- [ ] 3.1 Class parser + `hit-die-consistent`, `level-table-read`, `proficiency-bonus-steps`, `features-match-table`; synthesized `classFeatures` refs
- [ ] 3.2 Subclass parser (PHB in-class, XGE/TCE groups) + `parent-class-known`, `feature-levels-match-class`; 6-part refs
- [ ] 3.3 Race and Subrace parser + `ability-increase-parsed`, `size-valid`, `speed-valid`, `parent-race-known`
- [ ] 3.4 Feat parser + `prerequisite-parsed`
- [ ] 3.5 Background parser + `skills-valid`, `feature-present`
- [ ] 3.6 Register all six in `assemble._PARSERS`; ids per design (subrace `<race>-<subrace>`)

## 4. Whole-book fix rounds

- [ ] 4.1 Score all 16 books for the six types; fix rounds until the done bar (≥90% found per book, zero unexplained wrong-and-accepted); Phase 1 types do not regress

## 5. App side

- [ ] 5.1 Confirm the six types render canonical text, ingest and show their fields on `/review`; fix gaps
- [ ] 5.2 C# test: every `classFeatures`/`subclassFeatures` ref in the new `phb14.json` parses with the coach's ref parser

## 6. Live run and finish

- [ ] 6.1 Rebuild `book-extractor` and `app`, run all 16 books live, confirm identical to local runs, commit canonical files
- [ ] 6.2 Coach smoke: level-up advice for a PHB class grounds on the new class entity (Ollama probe)
