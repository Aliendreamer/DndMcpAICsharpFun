# Programmatic Extraction Phase 2b (Equipment and Hazards) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** the book extractor also produces verified Weapon, Armor, Item, VehicleMount, Object, Trap, DiseasePoison and
Condition entities from all 16 books, in the existing 5etools-shaped field schemas.

**Architecture:** new parsers sit beside the Phase 1 and 2a parsers in `Tools/BookExtractor/bookextractor/parsers/`.
They share the `ParseContext` / `_PARSERS(body, ctx)` registry. A generalised `tables.py` turns printed table rows into
entities, joined to their same-name description paragraph. Each parser emits its own checks. The assembler adds the
common checks, ids, provenance, name repair and the `text-not-shared` gate.

**Tech Stack:** Python 3.12, rapidfuzz, pytest and jsonschema (`Tools/BookExtractor/.venv`). C# .NET 10 is only for
gap fixes.

**Spec:** `openspec/changes/programmatic-extraction-phase2b/`:

- `proposal.md`, `design.md`, `specs/*/spec.md`;
- the umbrella design in `openspec/changes/archive/2026-11-06-programmatic-extraction-phase2a/phase2-umbrella-design.md`.

**Local data (development only):** the scratchpad is
`S=/tmp/claude-1000/-home-aliendreamer-projects-DndMcpAICsharpFun/6ab16f2f-2ad0-495d-ad20-e45f0bab2658/scratchpad`.

- **OCR cache:** `$S/cache/<pdf-sha256>/ocr/NNN.json`. The hash per book is the `FileHash` column of
  `IngestionRecords`.
- **Corpus loop:**
  - `$S/runall.sh vN` re-parses all 16 books in about a minute;
  - `$S/scoreall.sh vN` (run from `Tools/BookExtractor`) writes the per-book/per-type table;
  - `.venv/bin/python $S/accepted_wrong13.py $S <repo> vN` is the wrong-and-accepted audit.
- **5etools truth:**
  - `items-base.json` (`baseitem`: weapons, armor, gear);
  - `items.json` (mundane items have `rarity` "none" or none; poisons carry `poisonTypes`);
  - `vehicles.json`, `objects.json`;
  - `trapshazards.json` (`trap`, `hazard`);
  - `conditionsdiseases.json` (`condition`, `disease`).

## Global Constraints

- No LLM and no GPU. 5etools is read only by `score.py` and by tests.
- Names come only from printed text: a row's label cell or a printed heading. Never guess one, and never take one from
  another page.
- A field the book does not print is absent, never guessed or inherited.
- D4: an entity with every check passing is Accepted, otherwise NeedsReview. Check reasons must be readable and name
  the values involved.
- Field shapes MUST validate against `Schemas/canonical/{Weapon,Armor,Item,VehicleMount,Object,Trap,DiseasePoison,Condition}Fields.schema.json`.
  `Object` is generated from C# `Domain/Entities/Fields/ObjectFields.cs`; the others carry `// SCHEMA-OVERRIDE`. Widen
  an override schema only for a printed 5etools shape, and report before touching a generated one.
- Ids are `<book-slug>.<type-lower>.<name-slug>`, where type-lower is `weapon`, `armor`, `item`, `vehiclemount`,
  `object`, `trap`, `diseasepoison` or `condition`.
- Values use 5etools encodings:
  - cost `value` is in copper (15 gp = 1500);
  - `weight` is in lb as a number;
  - `dmgType` is a letter code (S, P, B, …);
  - weapon `property` uses codes (V, F, L, T, H, 2H, R, A, LD, S);
  - `type` uses codes: M or R for weapons, LA, MA, HA or S for armor, G for gear, AT for artisan's tools, T for
    tools, INS for instruments, GS for gaming sets, MNT for mounts, TAH for tack, VEH for vehicles, TG for trade
    goods.
  - Read `items-base.json` `itemType` / `itemProperty` for the full code list.
- **Phase 1 and 2a must not regress.** After every parser change, run the whole-corpus loop:
  - every Monster, Spell, MagicItem, Class, Subclass, Race, Subrace, Feat and Background score line must be
    identical to the previous run;
  - no existing entity's disposition may change unless that is intended and explained.
- **Score text precision for every new type.** Run the final whole-change review (most capable model) before the
  live run. After the live run, verify `dnd_entities` per book by counting points per `source_book` and `data_source`
  against `IngestionRecords.EntityCount`.
- Repo rules:
  - work and commit on `main`;
  - Serena for `.cs` edits;
  - `git` and `dotnet` need `dangerouslyDisableSandbox`;
  - never `git stash`;
  - stage only your own files, never Config noise;
  - never read `Config/` or `.env*`;
  - never run OCR (every page is cached; a cache miss means stop and report).

## Review Focus

1. **A weapon or gear row whose name wraps onto two lines or carries a footnote marker** ("Net*",
   "Ball bearings (bag of 1,000)"). It becomes one entity named without the marker, and the marker is recorded.
   Test in Task 2.
2. **A table row that shares its name with an unrelated heading elsewhere** (a "Net" in a trap, a "Poisoner's Kit" in
   a background). The description join happens only inside the equipment section. Test in Task 4.
3. **A trap whose DC is printed in prose** ("DC 15 Wisdom (Perception) check"). It is parsed; when no DC is printed,
   the field is absent and the check passes honestly. Test in Task 6.
4. **A poison table row whose price is "—" or missing.** `cost-valid` passes for a printed dash and fails for an
   unreadable cell. Test in Task 7.
5. **The PHB conditions appendix running into the next appendix.** It stops at the appendix title. Test in Task 7.

---

### Task 1: Scorer truth for the eight types

**Files:** modify `bookextractor/score.py` and `tests/test_score.py`.

**Interfaces:**

- Produces:
  - `score.TYPES` extended with the eight types;
  - `load_truth`, `score_record` and text precision support them;
  - `available_types` returns them wherever truth exists.

**Truth selection:**

- Weapon: `baseitem` with `weapon` or `weaponCategory`, source equal to the book, no `_copy`.
- Armor: `baseitem` with `armor`, or type LA, MA, HA or S.
- Item: `baseitem` that is neither, plus `items.json` mundane items (rarity none or absent, not a poison, not a
  weapon or armor).
- VehicleMount: `vehicles.json`.
- Object: `objects.json`.
- Trap: `trap` plus `hazard`.
- DiseasePoison: `disease` plus `items.json` poisons (`poisonTypes`).
- Condition: `condition`. 5etools `status` records are not conditions; drop them and list them as known.

**Per-field agreement:**

- Weapon: `value`, `weight`, `dmg1`, `dmgType`, `property` set, `weaponCategory`.
- Armor: `ac`, `strength`, `stealth`, `value`, `weight`.
- Item: `value`, `weight`, `type`.
- Trap: name only, plus `trapHazType`.
- DiseasePoison: `poisonTypes`.
- Condition: name.

Text coverage and precision apply to every type, using `entries`.

- [ ] **Step 1: Failing tests** (real 5etools, skipped when absent):

```python
def test_truth_phb_weapons(): assert {t["name"] for t in load_truth("phb", FT, "Weapon")} >= {"Longsword", "Shortbow", "Net"}
def test_truth_phb_armor(): assert len(load_truth("phb", FT, "Armor")) == 13
def test_truth_conditions(): assert len(load_truth("phb", FT, "Condition")) == 15
def test_truth_dmg_poisons(): assert "Burnt Othur Fumes" in {t["name"] for t in load_truth("dmg", FT, "DiseasePoison")}
def test_score_longsword():
    t = next(t for t in load_truth("phb", FT, "Weapon") if t["name"] == "Longsword")
    r, *_ = score_record("Weapon", {"value": 1500, "weight": 3, "dmg1": "1d8", "dmgType": "S", "property": ["V"], "weaponCategory": "martial"}, t)
    assert all(r.values())
```

- [ ] **Step 2: Run the tests** and confirm they FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests and the full suite** and confirm they pass.
- [ ] **Step 5: Commit** `feat(bookextractor): scorer truth for equipment and hazard types`.

### Task 2: Generalised table reader

**Files:** modify `bookextractor/tables.py`, `tests/test_tables.py`; fixtures for the PHB weapons, armor and
adventuring gear table pages.

**Interfaces:**

- Produces `read_rows(page: Page, headers: list[str]) -> RowTable | None`, where:
  - `RowTable` is `headers`, `rows: list[Row]`, `page`, `box`;
  - `Row` is `label: str`, `cells: dict[str, str]`, `group: str | None`, `marker: str | None`, `lines: list[Line]`.
- Rows anchor on the first column's label lines. A label that wraps onto the next line with no cells of its own is
  joined to it.
- The group comes from a full-width sub-heading row ("Simple Melee Weapons").
- Footnote markers (`*`, `†`) are stripped into `marker`.
- Keep `read_table`, the class reader, unchanged.

- [ ] **Step 1: Failing tests:**

```python
def test_phb_weapons(): rows = read_rows(page(<weapons page>), ["Name", "Cost", "Damage", "Weight", "Properties"]); r = row(rows, "Longsword"); assert r.cells["Cost"] == "15 gp" and r.group.startswith("Martial Melee")
def test_phb_armor(): r = row(read_rows(page(<armor page>), ["Armor", "Cost", "Armor Class (AC)", "Strength", "Stealth", "Weight"]), "Chain mail"); assert r.cells["Strength"] == "Str 13"
def test_wrapped_label_and_marker(): ...  # a gear row with a footnote marker and a two-line label is one Row
def test_class_tables_unchanged(): ...    # existing read_table tests still pass
```

- [ ] **Step 2: Run the tests** and confirm they FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests and the full suite** and confirm they pass.
- [ ] **Step 5: Commit** `feat(bookextractor): table rows as entities`.

### Task 3: Weapon and Armor parsers and checks

**Files:** create `parsers/equipment.py` and `tests/test_equipment.py`; fixtures for PHB weapons and armor, and DMG
Renaissance/Modern weapons.

**Interfaces:**

- Produces:
  - `parse_weapons(body, ctx)` and `parse_armor(body, ctx)`;
  - the shared check helpers `cost_check(cell)`, `weight_check(cell)`, `damage_check(cell)` and
    `properties_check(list)` in `parsers/equipment.py`.
- The check names are `cost-valid`, `weight-valid`, `damage-valid`, `properties-known` and `ac-valid`.
- Table pages are found by their header row, never by page number.

- [ ] **Step 1: Failing tests** use the spec scenarios: Longsword and Chain mail with exact values, the
  "Finesse, Lihgt" property failure naming "Lihgt", the "1S gp" cost failure, and schema validation.
- [ ] **Step 2: Run the tests** and confirm they FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests and the full suite** and confirm they pass.
- [ ] **Step 5: Commit** `feat(bookextractor): weapon and armor parsers`.

### Task 4: Item parser (gear, tools, packs, mounts, tack, trade goods) with the description join

**Files:** extend `parsers/equipment.py` and its tests; fixtures for the PHB gear table and description pages, and
the tools, mounts and trade goods tables.

**Interfaces:**

- Produces `parse_items_mundane(body, ctx)`, which emits `type="Item"` with the 5etools `type` code from its table.
- The join: description paragraphs whose bold or heading lead-in equals the row label (casing and plural tolerant)
  inside the same chapter section become `entries`.
- Equipment packs (Burglar's Pack and the rest) are Items whose `entries` list their printed contents.

- [ ] **Step 1: Failing tests:**
  - Crowbar has `value` 200, `weight` 5, and `entries` that start "Using a crowbar".
  - A same-name heading outside the equipment section is not joined (Review Focus 2).
  - Explorer's Pack lists its contents.
  - Schema validation.
- [ ] **Step 2: Run the tests** and confirm they FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests and the full suite** and confirm they pass.
- [ ] **Step 5: Commit** `feat(bookextractor): mundane item parser`.

### Task 5: VehicleMount and Object parsers

**Files:** create `parsers/vehicle_object.py` and its tests; fixtures for the DMG vehicles/siege pages and MTF
infernal war machines.

**Interfaces:**

- Produces `parse_vehicles(body, ctx)` and `parse_objects(body, ctx)`.
- Vehicles come from printed statistics: speed, crew, passengers, cargo, AC, HP, damage threshold. Objects come from
  stat blocks with AC, HP, immunities and actions.
- Find 5etools truth names per book from `vehicles.json` and `objects.json`.

- [ ] **Step 1: Failing tests:** one DMG vehicle and one siege object (for example a ballista) with exact printed
  values, plus schema validation.
- [ ] **Step 2: Run the tests** and confirm they FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests and the full suite** and confirm they pass.
- [ ] **Step 5: Commit** `feat(bookextractor): vehicle and object parsers`.

### Task 6: Trap parser (traps and hazards)

**Files:** create `parsers/trap.py` and its tests; fixtures for DMG traps (Collapsing Roof, Poison Needle), XGE traps,
and DMG wilderness hazards.

**Interfaces:**

- Produces `parse_traps(body, ctx)`, with `trapHazType` (`MECH`, `MAG`, `WTH`, `ENV`, `HAZ`, … following
  `trapshazards.json`) and `entries`.
- The DCs (`detectDc`, `disarmDc`) are recorded only where printed.
- Check: `trap-dcs-parsed`.

- [ ] **Step 1: Failing tests:** Collapsing Roof found with its printed DCs; a DC printed in prose is parsed (Review
  Focus 3); a trap with no printed DC passes with the field absent.
- [ ] **Step 2: Run the tests** and confirm they FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests and the full suite** and confirm they pass.
- [ ] **Step 5: Commit** `feat(bookextractor): trap and hazard parser`.

### Task 7: DiseasePoison and Condition parsers

**Files:** create `parsers/hazard_rules.py` and its tests; fixtures for the DMG poisons table and descriptions, DMG
diseases, and PHB Appendix A.

**Interfaces:**

- Produces `parse_diseases_poisons(body, ctx)`, which emits poisons from table rows joined to their descriptions,
  with `poisonTypes` and cost, plus diseases with their entries.
- Produces `parse_conditions(body, ctx)`, which emits the 15 conditions from Appendix A, ending at the appendix title.
- Checks: `poison-type-known`, `cost-valid` and `condition-known`.

- [ ] **Step 1: Failing tests:**
  - Burnt Othur Fumes has `poisonTypes` ["inhaled"] and `value` 50000.
  - All 15 conditions are found, and "Unconscious" does not run into Appendix B (Review Focus 5).
  - A poison price printed as "—" passes, and an unreadable price fails (Review Focus 4).
- [ ] **Step 2: Run the tests** and confirm they FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests and the full suite** and confirm they pass.
- [ ] **Step 5: Commit** `feat(bookextractor): disease, poison and condition parsers`.

### Task 8: Register the eight types

**Files:** modify `assemble.py` (`ALL_TYPES`, `_PARSERS`) and `report.TYPES`, with a test that
`report.TYPES == ALL_TYPES`; also `test_assemble.py` and `test_service.py`.

- [ ] **Step 1: Failing tests:**
  - The PHB fixture extraction yields `phb14.weapon.longsword`, `phb14.armor.chain-mail`, `phb14.item.crowbar` and
    `phb14.condition.poisoned`.
  - Every entity validates against its schema.
  - No id repeats.
  - The `text-not-shared` gate runs over the new types.
- [ ] **Step 2: Run the tests** and confirm they FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests and the full suite** and confirm they pass.
- [ ] **Step 5: Commit** `feat(bookextractor): register equipment and hazard types`.

### Task 9: Whole-book fix rounds to the done bar (controller-run)

- [ ] Run `runall.sh v22`, `scoreall.sh v22` and the audit.
- [ ] For each book type below 90%, each wrong-and-accepted entity, and each entity with low text precision, run a
  fix round:
  - a fixture and a failing test first;
  - then the fix;
  - then the full suite;
  - then a corpus re-run, in which every earlier type's lines must stay identical.
- [ ] Stop at the done bar. Record each residue in the ledger, with which source is right.

### Task 10: App side

- [ ] Add a C# test that `EntityCanonicalTextDispatcher` renders non-empty text for one entity of each of the eight
  types, from a v-latest fixture. Fix any renderer gap.
- [ ] `/review` is generic (`Fields.EnumerateObject`), so confirm only.
- [ ] Run the full `dotnet test`. The known `ManualCasesTests` failure is pre-existing.

### Task 11: Final review, live run (controller-run)

- [ ] Run the final whole-branch review on the most capable model, then one fix wave and a scoped re-review.
- [ ] Rebuild `book-extractor` and `app`, then queue all 16 books.
- [ ] Confirm the live files equal the latest local run.
- [ ] Verify `dnd_entities`: per-book counts equal `EntityCount`, and every point is programmatic.
- [ ] Commit the canonical files.
