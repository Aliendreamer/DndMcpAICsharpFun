## 1. Truth and scorer

- [ ] 1.1 Scorer truth + per-field agreement + text precision for Weapon, Armor, Item, VehicleMount, Object, Trap, DiseasePoison, Condition; audit covers them

## 2. Table reader

- [ ] 2.1 Generalise `tables.py`: rows anchored on first-column labels, grouped sub-heading categories, footnote markers

## 3. Parsers and checks

- [ ] 3.1 Weapon + Armor (PHB, DMG renaissance/modern, ERLW, SCAG)
- [ ] 3.2 Item: adventuring gear, tools, packs, mounts/tack, trade goods; description join
- [ ] 3.3 VehicleMount + Object
- [ ] 3.4 Trap (traps + hazards)
- [ ] 3.5 DiseasePoison + Condition
- [ ] 3.6 Register all eight types

## 4. Whole-book fix rounds

- [ ] 4.1 Score all 16 books; fix rounds to the done bar; Phase 1 and 2a lines unchanged

## 5. App side

- [ ] 5.1 The eight types render canonical text, ingest and show on `/review`

## 6. Final review, live run, finish

- [ ] 6.1 Final whole-change review before the live run; fixes
- [ ] 6.2 Rebuild, run all 16 live, verify dnd_entities per book, commit canonical files
