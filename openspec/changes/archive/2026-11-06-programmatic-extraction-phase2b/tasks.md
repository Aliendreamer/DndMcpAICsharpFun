## 1. Truth and scorer

- [x] 1.1 Scorer truth + per-field agreement + text precision for Weapon, Armor, Item, VehicleMount, Object, Trap, DiseasePoison, Condition; audit covers them

## 2. Table reader

- [x] 2.1 Generalise `tables.py`: rows anchored on first-column labels, grouped sub-heading categories, footnote markers

## 3. Parsers and checks

- [x] 3.1 Weapon + Armor (PHB, DMG renaissance/modern, ERLW, SCAG)
- [x] 3.2 Item: adventuring gear, tools, packs, mounts/tack, trade goods; description join
- [x] 3.3 VehicleMount + Object
- [x] 3.4 Trap (traps + hazards)
- [x] 3.5 DiseasePoison + Condition
- [x] 3.6 Register all eight types

## 4. Whole-book fix rounds

- [x] 4.1 Score all 16 books; fix rounds to the done bar; Phase 1 and 2a lines unchanged

## 5. App side

- [x] 5.1 The eight types render canonical text, ingest and show on `/review`

## 6. Final review, live run, finish

- [x] 6.1 Final whole-change review before the live run; fixes
- [x] 6.2 Rebuild, run all 16 live, verify dnd_entities per book, commit canonical files
