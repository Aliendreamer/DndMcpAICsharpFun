# Programmatic Extraction Phase 2a (Character Data) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** the Python book extractor also produces verified Class, Subclass, Race, Subrace, Feat and Background
entities from all 16 books. They come out in the existing `*Fields` shapes, so the character coach works again with no
coach code change.

**Architecture:**

- Six new parsers sit next to Phase 1's monster, spell and item parsers in `Tools/BookExtractor/bookextractor/parsers/`.
- They take a `ParseContext` that carries the book's 5etools source key and the raw pages.
- A new `tables.py` reads class level tables from OCR boxes.
- Each parser emits its own type checks. The assembler adds the common checks, ids and provenance as before.
- The development scorer gets 5etools truth for the six types.
- The C# side already has renderers and schemas, so it only gets a ref-parse test and gap fixes.

**Tech Stack:** Python 3.12, rapidfuzz, pytest and jsonschema (existing venv `Tools/BookExtractor/.venv`). .NET 10,
xunit and FluentAssertions.

**Spec:** `openspec/changes/programmatic-extraction-phase2a/`:

- `proposal.md`;
- `design.md`;
- `phase2-umbrella-design.md`;
- `specs/programmatic-character-extraction/spec.md`;
- `specs/programmatic-book-extraction/spec.md`.

**Local data (development only):**

- The OCR cache copy for all 16 books is at
  `S=/tmp/claude-1000/-home-aliendreamer-projects-DndMcpAICsharpFun/6ab16f2f-2ad0-495d-ad20-e45f0bab2658/scratchpad`,
  in `$S/cache/<pdf-sha256>/ocr/NNN.json`.
- `$S/allbooks.txt` lists `slug:pdf-name` pairs. The cache directory for a book is the `FileHash` column of
  `IngestionRecords`, or find it by grepping the cache for a known heading.
- Re-parse a whole book:
  `.venv/bin/python -m bookextractor extract $R/books/<pdf-name>.pdf --slug S --cache $S/cache --out $S/vN/S.json`
- `$S/scoreall.sh vN` writes the per-book score table.
- `$S/accepted_wrong12.py $S $R` runs the wrong-and-accepted audit.
- 5etools truth is in `5etools/`: `class/class-<name>.json`, `races.json` (`race`, `subrace`), `feats.json`,
  `backgrounds.json`.

## Global Constraints

- No LLM and no GPU. 5etools is read only by `score.py` and by tests, never by `assemble`, the parsers or the service.
- Names come only from printed headings. Never guess a name, and never take one from another page. A missing name
  gives a failed `name-found` check; the assembler already does this.
- D4: the assembler sets nothing new. Disposition is all checks passed → Accepted, otherwise NeedsReview, as in the C#
  `OverrideMerger`. Every check reason is human-readable and names the values involved.
- Field shapes MUST validate against `Schemas/canonical/{Class,Subclass,Race,Subrace,Feat,Background}Fields.schema.json`.
  A field the book does not print is null or absent, never guessed.
- Ids are `<book-slug>.<type-lower>.<name-slug>` (`class`, `subclass`, `race`, `subrace`, `feat`, `background`).
  A subrace's id name is `<race>-<subrace>`: the name slug of `"{raceName} {subrace name}"`, e.g.
  `phb14.subrace.dwarf-hill-dwarf`.
- Feature refs follow the 5etools convention exactly:
  - `classFeatures` are strings `Name|Class|ClassSource|Level`, with `ClassSource` empty when the class is defined in
    PHB (`Rage|Barbarian||1`).
  - A level that gains a subclass feature is `{"classFeature": "<ref>", "gainSubclassFeature": true}`.
  - `subclassFeatures` are `Name|Class|ClassSource|ShortName|SubclassSource|Level`
    (`Spirit Shield|Barbarian||Ancestral Guardian|XGE|6`).
- Phase 1 must not regress. After every parser change, run all-book `scoreall.sh` and diff against the previous
  version for Monster, Spell and MagicItem.
- Repo rules:
  - work and commit on `main`;
  - Serena for `.cs` edits;
  - `dotnet` and `git` need `dangerouslyDisableSandbox` (git-crypt);
  - never read `Config/appsettings*.json` or `.env*`;
  - never stage Config noise;
  - never `git stash`;
  - markdown lint clean;
  - CPU only.

## Review Focus

1. **A class whose level table spans a column break or is OCR'd with merged cells.** The class is still produced,
   `level-table-read` fails naming the class, and it is NeedsReview. Test in Task 3.
2. **A subrace heading with no traits under it** (lore heading inside the race section, e.g. "Dwarf Names"). It is not
   a subrace, so no entity. Test in Task 5.
3. **A feat-like heading outside a feats section** (a class feature named like a feat, e.g. "Great Weapon Fighting").
   It is not a feat. Test in Task 6.
4. **A background whose "Skill Proficiencies:" line wraps onto a second line.** Both skills are read and
   `skills-valid` passes. Test in Task 7.
5. **A supplement subclass for a class whose subclass-group heading is OCR'd with casing noise** ("PRIMAL PATHs").
   It is still found by fuzzy match. Test in Task 4.

---

### Task 1: Scorer truth for the six character types

**Files:**

- Modify: `Tools/BookExtractor/bookextractor/score.py`, `Tools/BookExtractor/bookextractor/cli.py`
- Test: `Tools/BookExtractor/tests/test_score.py`

**Interfaces:**

- Produces:
  - `score.TYPES` extended with `"Class", "Subclass", "Race", "Subrace", "Feat", "Background"`;
  - `load_truth(book, fivetools_dir, etype)` handles them;
  - `score_record(etype, fields, t)` returns per-field agreement for them;
  - `available_types(book, dir)` returns, for **every** book including the core three, every type with truth. Core
    books keep scoring their Phase 1 type as well.
  - The `score --type` CLI choices come from `score.TYPES`, not `assemble.ALL_TYPES`.

- [ ] **Step 1: Write failing tests** in `test_score.py`. They read the real `5etools/` directory and are skipped if it
  is absent.

```python
def test_truth_phb_classes(): assert {t["name"] for t in load_truth("phb", FT, "Class")} >= {"Barbarian", "Wizard"} and len(load_truth("phb", FT, "Class")) == 12
def test_truth_xge_subclasses(): assert "Path of the Zealot" in {t["name"] for t in load_truth("xge", FT, "Subclass")}
def test_truth_phb_subraces_named(): assert all(t.get("name") for t in load_truth("phb", FT, "Subrace"))   # nameless base subraces dropped
def test_truth_phb_feats_backgrounds(): assert len(load_truth("phb", FT, "Feat")) == 42 and "Acolyte" in {t["name"] for t in load_truth("phb", FT, "Background")}
def test_score_class_fields():
    t = next(t for t in load_truth("phb", FT, "Class") if t["name"] == "Barbarian")
    r, *_ = score_record("Class", {"hd": {"number": 1, "faces": 12}, "proficiency": ["str", "con"],
                                   "subclassTitle": "Primal Path", "classFeatures": t["classFeatures"]}, t)
    assert all(r.values())
def test_available_types_core_book_includes_character_types(): assert {"Spell", "Class", "Feat"} <= set(available_types("phb", FT))
```

- [ ] **Step 2: Run the tests and confirm they fail.**
  `cd Tools/BookExtractor && .venv/bin/pytest tests/test_score.py -q` should FAIL.

- [ ] **Step 3: Implement.** Truth selection:
  - Class: records in `class/class-*.json` → `class` with `source == BOOK`.
  - Subclass: `subclass` with `source == BOOK`.
  - Race: `races.json` `race`, with `source == BOOK` and no `_copy`.
  - Subrace: `subrace` with `source == BOOK` and a `name`. Match key is `key(f"{raceName} {name}")`, against our
    `key(name)` with the race word added if it is missing.
  - Feat and Background: `source == BOOK`.

  Per-field agreement:
  - Class: `hd` (faces), `proficiency`, `subclassTitle`, and `features`, which is the set of (name, level) from refs
    whose 5th part is absent or equals BOOK.
  - Subclass: `className`, `shortName`, and the feature-level set.
  - Race: `size`, `speed` walk, `ability`, `darkvision`.
  - Subrace: `raceName`, `ability`.
  - Feat: prerequisite presence.
  - Background: the `skillProficiencies` set. 5etools uses `[{"insight": true, ...}]`; compare lower-cased names.

  Text coverage uses `entries` through the existing `coverage()`.

- [ ] **Step 4: Run the tests and confirm they pass**, along with the full suite.
  `.venv/bin/pytest -q` should pass everything (199 existing tests plus the new ones).

- [ ] **Step 5: Commit**: `feat(bookextractor): scorer truth for class, subclass, race, subrace, feat, background`

### Task 2: Parse context, shared vocabulary, table reader

**Files:**

- Create: `bookextractor/tables.py`, `bookextractor/parsers/charcommon.py`
- Modify: `bookextractor/parsers/__init__.py`, `bookextractor/assemble.py`
- Test: `tests/test_tables.py`
- Fixtures: `tests/fixtures/ocr/phb/` (add the Fighter and Wizard table pages and the Barbarian table page), with
  `tests/fixtures/ocr/PAGES.md` updated.

**Interfaces:**

- Produces:
  - `ParseContext` dataclass in `parsers/__init__.py`: `slug: str`, `source: str` (5etools key, e.g. `"PHB"`), and
    `pages: dict[int, Page]` (raw, un-ordered pages).
  - `assemble.SOURCE_BY_SLUG`: phb14→PHB, mm14→MM, dmg14→DMG, mpmm→MPMM, mtf→MTF, ftd→FTD, bgg→BGG, vrgr→VRGR, egw→EGW,
    erlw→ERLW, ggr→GGR, scc→SCC, xge→XGE, tce→TCE, scag→SCAG, elemental-evil-player-s-companion→EEPC. An unknown slug
    gives `source = slug.upper()`.
  - `_PARSERS` values become `Callable[[list[Line], ParseContext], list[Parsed]]`. The Phase 1 parsers are wrapped as
    `lambda body, ctx: parse_monsters(body)`.
  - `tables.Table` dataclass `headers: list[str]`, `rows: list[list[str]]`, `page: int`, `lines: list[Line]`.
  - `tables.read_table(page: Page, first_header: str) -> Table | None`. Find the header row by fuzzy match (≥85) of
    `first_header` (e.g. `"Level"`). Columns are the header boxes' x-spans; rows cluster by y-overlap below the
    header. Each cell is the text of the lines whose x-centre falls in a column span, with `fix_digits` applied. Stop
    at the first row whose first cell is not an ordinal level (`1st`…`20th`, OCR-repaired). Return None when there is
    no header.
  - `charcommon`:
    - `SKILLS`: the 18 skill names.
    - `SIZES`: `{"Tiny":"T","Small":"S","Medium":"M","Large":"L","Huge":"H","Gargantuan":"G"}`.
    - `ABILITIES`: name→`str`/`dex`/….
    - `SUBCLASS_GROUPS`: class → (singular title, plural heading). Barbarian Primal Path, Bard Bard College, Cleric
      Divine Domain, Druid Druid Circle, Fighter Martial Archetype, Monk Monastic Tradition, Paladin Sacred Oath,
      Ranger Ranger Archetype, Rogue Roguish Archetype, Sorcerer Sorcerous Origin, Warlock Otherworldly Patron, Wizard
      Arcane Tradition, Artificer Artificer Specialist. The plural adds "s".
    - `SUBCLASS_LEVELS`: Barbarian [3,6,10,14], Bard [3,6,14], Cleric [1,2,6,8,17], Druid [2,6,10,14], Fighter
      [3,7,10,15,18], Monk [3,6,11,17], Paladin [3,7,15,20], Ranger [3,7,11,15], Rogue [3,9,13,17], Sorcerer
      [1,6,14,18], Warlock [1,6,10,14], Wizard [2,6,10,14], Artificer [3,5,9,15].
    - `CLASS_SOURCE`: the 12 PHB classes → `""`, Artificer → `"TCE"`. Exception: when the class is defined in the book
      being parsed, `ctx.source`, unless that source is PHB.
    - `ordinal(text) -> int | None`: "3rd" → 3, OCR-tolerant ("lst" → 1).

- [ ] **Step 1: Write failing tests** in `tests/test_tables.py`:

```python
def test_fighter_table():
    t = read_table(page(<fighter table page>), "Level")
    assert len(t.rows) == 20 and t.rows[0][1] == "+2" and t.rows[19][1] == "+6"
    assert "Action Surge" in t.rows[1][2]
def test_wizard_table_spell_columns():
    t = read_table(page(<wizard table page>), "Level")
    assert len(t.rows) == 20 and len(t.headers) >= 12 and t.rows[0][3] == "3"   # cantrips known at 1st
def test_no_table_on_prose_page(): assert read_table(page(<a PHB prose page>), "Level") is None
def test_subclass_levels_match_5etools():   # skipped if 5etools/ is absent
    for cls, levels in SUBCLASS_LEVELS.items(): assert levels == gain_levels_from_5etools(cls)
def test_ordinal(): assert [ordinal(x) for x in ("1st", "lst", "2nd", "3rd", "20th", "x")] == [1, 1, 2, 3, 20, None]
```

  The page numbers come from grepping the PHB cache for the table headers. Record them in `PAGES.md`. The
  `test_subclass_levels_match_5etools` helper reads the PHB class's (TCE for Artificer) `gainSubclassFeature` levels.

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement** `tables.py`, `charcommon.py`, `ParseContext`, `SOURCE_BY_SLUG` and the `_PARSERS` call
  change. `extract_book` builds the `ParseContext` from `book_slug` and `by_number`.
- [ ] **Step 4: Run the tests and confirm they pass.** `.venv/bin/pytest -q` should be all green, including Phase 1.
- [ ] **Step 5: Commit**: `feat(bookextractor): parse context, class table reader, character vocabulary`

### Task 3: Class parser and checks

**Files:**

- Create: `bookextractor/parsers/charclass.py`
- Test: `tests/test_charclass.py`
- Fixtures: the PHB Barbarian, Fighter and Wizard chapter pages, plus the TCE Artificer class pages.

**Interfaces:**

- Consumes: `ParseContext`, `read_table`, `charcommon` (Task 2).
- Produces: `parse_classes(body: list[Line], ctx: ParseContext) -> list[Parsed]` with `type="Class"`. Also produces
  `class_checks(fields: dict, table: Table | None, feature_headings: list[str], text: str) -> list[Check]`, which
  returns, in order, `hit-die-consistent`, `level-table-read`, `proficiency-bonus-steps` and `features-match-table`.

- [ ] **Step 1: Write failing tests.**

```python
def test_barbarian():
    p = get_class("Barbarian")
    f = p.fields
    assert f["hd"] == {"number": 1, "faces": 12} and f["proficiency"] == ["str", "con"]
    assert f["subclassTitle"] == "Primal Path" and "Rage|Barbarian||1" in f["classFeatures"]
    assert {"classFeature": "Primal Path|Barbarian||3", "gainSubclassFeature": True} in f["classFeatures"]
    assert not failed(p), failed(p)
def test_fighter_features_by_level(): assert "Action Surge|Fighter||2" in get_class("Fighter").fields["classFeatures"]
def test_wizard_passes_all_checks(): assert not failed(get_class("Wizard"))
def test_artificer_tce_source(): assert any(r.startswith("Magical Tinkering|Artificer|TCE|1") for r in refs(get_class("Artificer", book="tce")))
def test_hit_die_disagreement():   # spec scenario: Hit Dice 1d10 but "12 + your Constitution modifier"
    c = check(class_checks(fields_with(hd_faces=10), table_ok, headings_ok, text_with("Hit Points at 1st Level: 12 + your Constitution modifier")), "hit-die-consistent")
    assert not c.passed and "1d10" in c.reason and "12" in c.reason
def test_unreadable_table_still_produced():   # Review Focus 1
    p = parse_classes(barbarian_lines_without_table_page(), CTX)[0]
    assert p.name == "Barbarian" and not check(p.checks, "level-table-read").passed and "Barbarian" in check(p.checks, "level-table-read").reason
def test_class_fields_validate_schema(): jsonschema.validate(get_class("Barbarian").fields, schema("ClassFields"))
```

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement.**
  - Anchor: a line fuzzy-matching `Hit Dice:` after a `Class Features` heading. The class name is the nearest
    chapter-level heading above (tallest heading before "Class Features" on the same or previous two pages), and the
    name must be a key of `SUBCLASS_GROUPS`. If it isn't, there is no name; `name-found` fails.
  - Fields: `hd`; saving throws from "Saving Throws:"; `startingProficiencies` (armor, weapons, tools, and skills
    "Choose N from …") in the 5etools shape; `multiclassing` only if the book prints it there (PHB prints it in
    chapter 6, so leave it null).
  - `subclassTitle`: the singular from `SUBCLASS_GROUPS`, but only when the class text names it.
  - `classFeatures`: from the table's Features column, with refs per the Global Constraints. The class's
    subclass-feature levels become `gainSubclassFeature` objects.
  - `entries`: the feature texts.
  - Text runs from the class heading to the subclass-group heading.
- [ ] **Step 4: Run the tests and confirm they pass.** Then score the PHB locally
  (`extract … --types Class` after Task 8, or call `parse_classes` on the cached book in a scratch script). Expect
  12/12 classes found.
- [ ] **Step 5: Commit**: `feat(bookextractor): class parser with level table and class checks`

### Task 4: Subclass parser and checks

**Files:**

- Create: `bookextractor/parsers/subclass.py`
- Test: `tests/test_subclass.py`
- Fixtures: the PHB Path of the Berserker pages, and the XGE Path of the Zealot pages plus their "Primal Paths" heading
  page.

**Interfaces:**

- Consumes: `ParseContext`, `charcommon`.
- Produces: `parse_subclasses(body, ctx) -> list[Parsed]` with `type="Subclass"`. Also produces
  `subclass_checks(fields: dict) -> list[Check]`, which returns `parent-class-known` and
  `feature-levels-match-class`.

- [ ] **Step 1: Write failing tests.**

```python
def test_zealot():
    p = get_sub("Path of the Zealot", book="xge")
    assert p.fields["className"] == "Barbarian" and p.fields["classSource"] == "PHB" and p.fields["shortName"] == "Zealot"
    assert sorted({int(r.split("|")[-1]) for r in p.fields["subclassFeatures"]}) == [3, 6, 10, 14]
    assert any(r.endswith("|Barbarian||Zealot|XGE|3") for r in p.fields["subclassFeatures"])
    assert not failed(p)
def test_berserker_phb(): assert get_sub("Path of the Berserker").fields["className"] == "Barbarian"
def test_group_heading_casing_noise():   # Review Focus 5
    assert get_sub("Path of the Zealot", book="xge", mutate_heading="PRIMAL PATHs")
def test_feature_levels_mismatch():
    c = check(subclass_checks({"className": "Barbarian", "subclassFeatures": ["A|Barbarian||X|XGE|3", "B|Barbarian||X|XGE|5"]}), "feature-levels-match-class")
    assert not c.passed and "5" in c.reason
def test_subclass_schema(): jsonschema.validate(get_sub("Path of the Zealot", book="xge").fields, schema("SubclassFields"))
```

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement.**
  - Locate each plural group heading from `SUBCLASS_GROUPS` (fuzzy ≥85, case-insensitive). The parent class is the
    group's class.
  - Each subsequent heading taller than body text, and not itself a feature heading, starts a subclass. A heading is
    a subclass when, within its section, a line has an `ordinal` level cue ("At 3rd level", "Starting at 6th level",
    "Beginning at 10th level"). The section ends at the next subclass heading or the next group or class heading.
  - Each feature heading inside it gets the level from its cue line.
  - `shortName`: the name minus the group's leading phrase ("Path of the ", "College of ", "… Domain",
    "Circle of the ", "Way of the ", "Oath of ", "School of ", "The ", "Way of ").
  - `classSource` follows the `CLASS_SOURCE` rule: PHB gives `"PHB"` in the field (the 5etools `classSource` value)
    and an empty string inside the refs.
- [ ] **Step 4: Run the tests and confirm they pass**, then the whole suite.
- [ ] **Step 5: Commit**: `feat(bookextractor): subclass parser and checks`

### Task 5: Race and subrace parser and checks

**Files:**

- Create: `bookextractor/parsers/race.py`
- Test: `tests/test_race.py`
- Fixtures: the PHB Dwarf pages and one MPMM race page (e.g. Goliath).

**Interfaces:**

- Produces: `parse_races(body, ctx) -> list[Parsed]`, which yields both `type="Race"` and `type="Subrace"` records.
  Also `race_checks(fields: dict, is_subrace: bool, known_races: set[str]) -> list[Check]`, which returns
  `ability-increase-parsed`, `size-valid` and `speed-valid`, plus `parent-race-known` for a subrace.
- `_PARSERS` registers `"Race"` and `"Subrace"`. Both call `parse_races` once per book (cache on `ctx`) and filter by
  type.

- [ ] **Step 1: Write failing tests.**

```python
def test_dwarf():
    d = get("Dwarf", "Race")
    assert d.fields["size"] == ["M"] and d.fields["speed"] == 25 and d.fields["darkvision"] == 60
    assert d.fields["ability"] == [{"con": 2}] and not failed(d)
def test_hill_dwarf():
    h = get("Hill Dwarf", "Subrace")
    assert h.fields["raceName"] == "Dwarf" and h.fields["ability"] == [{"wis": 1}] and not failed(h)
def test_lore_heading_is_not_a_subrace(): assert "Dwarf Names" not in names("Subrace")   # Review Focus 2
def test_mpmm_goliath(): assert get("Goliath", "Race", book="mpmm").fields["size"] == ["M"]
def test_speed_not_multiple_of_5():
    c = check(race_checks({"ability": [{"con": 2}], "size": ["M"], "speed": 27}, False, set()), "speed-valid")
    assert not c.passed and "27" in c.reason
def test_race_schemas(): jsonschema.validate(get("Dwarf","Race").fields, schema("RaceFields")); jsonschema.validate(get("Hill Dwarf","Subrace").fields, schema("SubraceFields"))
```

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement.**
  - `speed` uses the 5etools shape: a plain number when only walking speed is printed (`25`), else
    `{"walk": 30, "fly": 30}`.
  - Anchor: an "Ability Score Increase." trait line with "Size." or "Speed." within the next 15 lines. A race's name
    is the nearest heading above matching `<Name> Traits`, minus "Traits". Without that heading (MPMM prints "<Race>
    Traits" too), there is no name.
  - Subraces: after the race's trait run, any heading followed within 3 lines by its own "Ability Score Increase."
    trait. `raceName` is the enclosing race and `raceSource` is `ctx.source`.
  - MPMM-style races have "Ability Score Increase" text without amounts: `ability` is null and
    `ability-increase-parsed` passes when the text says increases are chosen ("increase one ability score by 2").
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit**: `feat(bookextractor): race and subrace parser and checks`

### Task 6: Feat parser and checks

**Files:**

- Create: `bookextractor/parsers/feat.py`
- Test: `tests/test_feat.py`
- Fixtures: two PHB feats pages (Heavily Armored and its neighbours) and one XGE racial feats page.

**Interfaces:**

- Produces: `parse_feats(body, ctx) -> list[Parsed]` with `type="Feat"`, fields `prerequisites: list[str]`,
  `description: str` and `grants: list[str]`. Also `feat_checks(fields, prerequisite_line: str | None) ->
  list[Check]`, which returns `prerequisite-parsed`.

- [ ] **Step 1: Write failing tests.**

```python
def test_heavily_armored():
    f = get("Heavily Armored")
    assert f.fields["prerequisites"] == ["Proficiency with medium armor"]
    assert any("Strength" in g for g in f.fields["grants"]) and not failed(f)
def test_feat_without_prerequisite(): assert get("Alert").fields["prerequisites"] == []
def test_xge_racial_feat(): assert get("Dwarven Fortitude", book="xge").fields["prerequisites"] == ["Dwarf"]
def test_class_feature_heading_not_a_feat(): assert "Great Weapon Fighting" not in names(book="phb_fighter_pages")   # Review Focus 3
def test_feat_schema(): jsonschema.validate(get("Alert").fields, schema("FeatFields"))
```

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement.**
  - A feats section starts at a heading fuzzy-matching `Feats` or `Racial Feats`, and ends at the next heading of
    equal or greater height. Each smaller heading inside it is a feat.
  - `Prerequisite:` lines are split on `, ` and ` or `. If the line exists but is empty, the check fails.
  - `grants` are the bullet lines (`•`, `.` bullets as OCR'd).
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit**: `feat(bookextractor): feat parser and checks`

### Task 7: Background parser and checks

**Files:**

- Create: `bookextractor/parsers/background.py`
- Test: `tests/test_background.py`
- Fixtures: the PHB Acolyte pages and one SCC or GGR background page.

**Interfaces:**

- Produces: `parse_backgrounds(body, ctx) -> list[Parsed]` with `type="Background"`, filling `BackgroundFields`
  (`skillProficiencies`, `toolProficiencies`, `languages`, `equipment`, `featureName`, `featureSummary`). Also
  `background_checks(fields) -> list[Check]`, which returns `skills-valid` and `feature-present`.

- [ ] **Step 1: Write failing tests.**

```python
def test_acolyte():
    b = get("Acolyte")
    assert b.fields["skillProficiencies"] == ["Insight", "Religion"] and b.fields["featureName"] == "Shelter of the Faithful"
    assert not failed(b)
def test_wrapped_skill_line(): assert len(get_wrapped_fixture().fields["skillProficiencies"]) == 2   # Review Focus 4 (synthetic two-line split of the Acolyte line)
def test_unknown_skill():   # spec scenario
    c = check(background_checks({"skillProficiencies": ["Insigth", "Religion"], "featureName": "X"}), "skills-valid")
    assert not c.passed and "Insigth" in c.reason
def test_supplement_background(): assert get(<SCC or GGR background name>, book=<book>).fields["featureName"]
def test_background_schema(): jsonschema.validate(get("Acolyte").fields, schema("BackgroundFields"))
```

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement.**
  - Anchor: a `Skill Proficiencies:` line; the name is the heading above.
  - Continuation lines join until the next `Label:` line.
  - The feature is the first heading starting with `Feature:` after the anchor.
  - Do not fuzzy-correct skills: `skills-valid` flags them. Names stay as printed.
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Commit**: `feat(bookextractor): background parser and checks`

### Task 8: Register the six types end to end

**Files:**

- Modify: `bookextractor/assemble.py` (`ALL_TYPES`, `_PARSERS`), `bookextractor/cli.py` (the `--types` help text),
  `tests/test_assemble.py`, `tests/test_service.py`

**Interfaces:**

- Produces: `ALL_TYPES = ("Monster", "Spell", "MagicItem", "Class", "Subclass", "Race", "Subrace", "Feat",
  "Background")`. The service default runs all of them.

- [ ] **Step 1: Write failing tests.** In `test_assemble.py`, extracting the PHB fixture pages (Barbarian, Dwarf,
  Acolyte, feats) with all types gives ids `phb14.class.barbarian`, `phb14.race.dwarf`,
  `phb14.subrace.dwarf-hill-dwarf`, `phb14.background.acolyte` and `phb14.feat.heavily-armored`. Every entity's
  fields validate against its schema, and no id repeats. In `test_service.py`, the default types include `"Class"`.
- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement the registration.**
- [ ] **Step 4: Run the tests and confirm they pass.** Run `.venv/bin/pytest -q` for the full suite.
- [ ] **Step 5: Commit**: `feat(bookextractor): register character types in the extractor`

### Task 9: Whole-book fix rounds to the done bar (controller-run, judgement)

**Files:** any of the six parsers, `tables.py`, `charcommon.py`, with new fixtures and tests per fix.

- [ ] **Step 1:** Re-parse all 16 books into `$S/v13/` and run `scoreall.sh v13`. Record the per-book table for the
  six types and the Phase 1 types in the ledger.
- [ ] **Step 2:** Extend `accepted_wrong12.py` → `accepted_wrong13.py` to cover the six types, and run it.
- [ ] **Step 3:** For each book below 90% found on a type, or each wrong-and-accepted entity, run a fix round. Each
  round does all of the following:
  - add a fixture page and a failing test for that layout;
  - fix the parser;
  - make the full suite green;
  - re-score all books (the Phase 1 types must not drop);
  - commit `fix(bookextractor): <what>`.
- [ ] **Step 4:** Stop when the done bar holds for every book: ≥90% of 5etools entries for each type found, and zero
  unexplained wrong-and-accepted (each residue is written down in the ledger with which source is right). Books with
  no 5etools truth for a type (SCAG, EEPC, ERLW Artificer) get a spot check of 10 entities against the page images,
  recorded in the ledger.

### Task 10: App side — rendering, ingest, review, coach refs

**Files:**

- Test: `DndMcpAICsharpFun.Tests/CharacterAdvice/ProgrammaticClassRefsTests.cs` (new)
- Modify: only what the checks below find missing (renderers in `Features/Entities/CanonicalText/`, the `/review`
  field display).

- [ ] **Step 1: Write a failing test.** Load `$S/v13/phb14.json`, copied as a test fixture to
  `DndMcpAICsharpFun.Tests/Fixtures/programmatic/phb14-classes.json` (Class and Subclass entities only). Assert:
  - for every Class, `ClassFeatureRefParser.Parse(fields.ClassFeatures, "classFeature")` returns as many refs as there
    are `classFeatures` items;
  - Barbarian's refs include ("Rage", "", 1);
  - for every Subclass, `Parse(fields.SubclassFeatures, "subclassFeature")` loses no ref;
  - `EntityCanonicalTextDispatcher` renders non-empty text for one entity of each of the six types.
- [ ] **Step 2: Run** `dotnet test --filter ProgrammaticClassRefsTests` with the sandbox disabled. Expect a FAIL until
  the fixture exists, or a real gap.
- [ ] **Step 3:** Fix any gap found. Open `/review` locally on a Class entity and confirm its fields are visible. If
  they are not, extend the field display for the six types.
- [ ] **Step 4: Run** the full `dotnet test` with the sandbox disabled. Everything should pass.
- [ ] **Step 5: Commit**: `test(coach): programmatic class refs parse with the coach's ref parser`

### Task 11: Live run and coach smoke (controller-run)

- [ ] **Step 1:** Run `docker compose up -d --build book-extractor app` with the sandbox disabled. Queue
  `extract-programmatic` for books 1–9 and 12–18 (key from the owner; never committed). Wait on the report
  modification times and the app log.
- [ ] **Step 2:** Confirm each live file's (type, name) set equals `$S/v13` (or the latest version), and that every
  `IngestionRecords.Status` is `EntitiesIngested`.
- [ ] **Step 3:** Commit `books/canonical/*` with
  `feat(canonical): programmatic character data for all 16 books`.
- [ ] **Step 4:** Coach smoke, using the Ollama probe technique from the dev-flow skill (warn the owner first: it uses
  the GPU). Ask for level-up advice for a level 2 → 3 PHB Barbarian. The tool result must cite `Primal Path` from
  `phb14.class.barbarian`.
- [ ] **Step 5:** Mark all of `tasks.md` done. Then follow the finish routine on the owner's "commit": commit →
  `openspec archive programmatic-extraction-phase2a -y` → skill-optimizer → roadmap memory.
