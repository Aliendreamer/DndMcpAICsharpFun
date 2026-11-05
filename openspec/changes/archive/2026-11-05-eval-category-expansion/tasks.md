## 1. Monster generator

- [x] 1.1 Monster templates (armor class, hit points, challenge rating) over reviewed `mm14` Monsters; unit-qualified
  answer variants; raw + answer tool-data variants; stable ids.
- [x] 1.2 Default `--books` = `phb14,mm14`.
- [x] 1.3 Tests: unreviewed monsters excluded; variants unit-qualified; same seed → same cases.

## 2. Manual cases

- [x] 2.1 Manual-case validation accepts `rules`, `lore`, `npc`, `not-found`.
- [x] 2.2 Add 5 rules, 4 lore, 2 npc, 2 not-found cases to `cases/manual.json`.
- [x] 2.3 Tests: the shipped manual file loads; a not-found case fails on an invented answer and passes on a
  could-not-find reply.

## 3. Report run detail

- [x] 3.1 Record called tool names and reply text (≤600 chars) per run; old reports still load.
- [x] 3.2 Tests: detail written; old-format report loads as a baseline.

## 4. Live baseline

- [x] 4.1 Full build + full test suite green; docs updated if flags/defaults changed (lint clean).
- [x] 4.2 Run the expanded eval live; commit the report; write `report.md` with per-category results and findings.
