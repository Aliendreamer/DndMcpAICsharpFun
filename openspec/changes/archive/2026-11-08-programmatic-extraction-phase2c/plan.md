# Programmatic Extraction Phase 2c (World) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** the book extractor also produces verified God, Rule, Plane, Faction, Location and Lore entities from all 16
books, in the existing override field schemas.

**Architecture:** new parsers in `Tools/BookExtractor/bookextractor/parsers/`:

- a shared section-tree reader, `sections.py`. It finds each chapter's entry heading level from heading widths and
  bounds sections at headings of equal or greater level or at chapter titles. It strips sidebars, art credits and
  running heads with the existing helpers.
- `god.py`: deity table rows via `tables.read_rows`, plus deity prose sections.
- `rule.py`.
- `world.py`: the section types, each typed by the nearest printed enclosing heading.

The assembler adds the common checks, ids, the text-not-shared gate and name repair, as in Phase 2a and 2b.

**Tech Stack:** Python 3.12, rapidfuzz, pytest, jsonschema (`Tools/BookExtractor/.venv`). C# .NET 10 for the render
test only.

**Spec:** `openspec/changes/programmatic-extraction-phase2c/` (`proposal.md`, `design.md`, `specs/*/spec.md`). Prior
art lives in the archived Phase 2a and 2b changes, which include `phase2-umbrella-design.md`.

**Local data (dev only):**

- The scratchpad is
  `S=/tmp/claude-1000/-home-aliendreamer-projects-DndMcpAICsharpFun/6ab16f2f-2ad0-495d-ad20-e45f0bab2658/scratchpad`.
- OCR cache: `$S/cache/<hash>/ocr/NNN.json`.
- Corpus loop: `$S/runall.sh vN`, then `$S/scoreall.sh vN` (run from `Tools/BookExtractor`), then
  `$S/accepted_wrong13.py $S <repo> vN`.
- 5etools truth:
  - `deities.json` (`deity`);
  - `variantrules.json` (`variantrule`);
  - `book/book-<key>.json`, the section tree, where `type` "section" or "entries" with a `name` is a heading.
- The latest corpus is v32.

## Global Constraints

- No LLM and no GPU. 5etools is read only by `score.py` and by tests.
- Names come only from printed headings, table labels or lead-ins. Fields only as printed; a field the book does not
  print stays absent. D4: an entity is Accepted only when every check passes. Check reasons name the values involved.
- Fields MUST validate against
  `Schemas/canonical/{God,Plane,Faction,Location,Lore,Rule}Fields.schema.json` (all `// SCHEMA-OVERRIDE`). Widen a
  schema only additively, for a printed 5etools shape.
- Ids are `<slug>.<type-lower>.<name-slug>`, with type-lower `god`, `rule`, `plane`, `faction`, `location` or `lore`.
- No text bleed. A section ends at a heading of equal or greater level, or at a chapter title. Sidebars, art credits
  and running heads are stripped. The text-not-shared gate applies; it stays one-sided for earlier types, so Phase
  1/2a/2b dispositions must not change.
- Score text precision as min and median for every type, and list every Accepted entity below 0.7.
- Whole-corpus loop after every parser change. Every earlier-type score line must stay identical, and no earlier-type
  disposition may change unless it is explained.
- Repo rules:
  - work and commit on `main`;
  - Serena for `.cs` edits;
  - `git` and `dotnet` need `dangerouslyDisableSandbox`;
  - never `git stash`;
  - stage only your own files, never Config;
  - never read `Config/` or `.env*`;
  - never run OCR.

## Review Focus

1. **A section that runs into the next chapter or a sidebar.** `text-complete` fails, and the text stops at the
   boundary. Test in Task 2.
2. **A deity row with a domain that OCR misread.** `domains-known` fails and names the word. Test in Task 3.
3. **A section whose enclosing headings carry conflicting type words** (a "City" under "Planes").
   `type-from-context` fails and names both words. Test in Task 5.
4. **A rules subsection printed as a bold lead-in rather than a heading.** It is not split into its own Rule unless
   the chapter's entry level says so. It stays inside its parent's text. Test in Task 4.
5. **A god section that contains a magic item or an NPC stat block.** Either the hosted entity's exemption applies (the
   host names it) or the gate fails. The host's text never silently absorbs another entity. Test in Task 3.

---

### Task 1: Scorer truth for God, Rule and the section types

**Files:** modify `bookextractor/score.py` and `tests/test_score.py`.

- **God**
  - Truth: `deities.json` records with the book's source and no `_copy`.
  - Match by name, plus pantheon when names repeat.
  - Fields:
    - `alignment` is an exact match;
    - `domains`: ours ⊆ truth, with the extra truth domains listed as a known difference ("domains added by later
      books");
    - `symbol` is a fuzzy match at 90 or above;
    - `pantheon` is an exact match.
  - Text precision is measured against `entries`.
- **Rule**
  - Truth: `variantrules.json`, by source.
  - Name keys ignore a "Variant:" or "Optional Rule:" prefix.
  - Field: `ruleType`.
- **Section types (Plane, Faction, Location, Lore)**
  - Truth is the named sections of `book-<key>.json`. Combine the four types for found/expected.
  - Recall counts headings at the level our entities use. Their per-chapter depth is supplied by the extracted
    entities' provenance; score at the heading depth where most of a chapter's entities match.
  - Precision is measured against each section's flattened entries.
  - Per-type found counts are informational.
- Known-difference lines appear wherever truth is dropped. `available_types` returns them wherever truth exists.
- Tests come first and run on real 5etools data, skipped when it is absent:
  - PHB has 195 deities, and Anubis matches.
  - PHB's "Customizing Ability Scores" has ruleType V.
  - The section-tree reader returns named sections for book-ggr.
- Earlier score lines must stay identical: re-score v32 without re-parsing.
- Commit: `feat(bookextractor): scorer truth for world types`.

### Task 2: Shared section reader

**Files:** create `bookextractor/parsers/sections.py` and `tests/test_sections.py`.

- Produces `read_sections(body, ctx) -> list[Section]`.
  - `Section` has `title: str`, `level: int`, `parents: list[str]` (the printed enclosing headings), `lines`, `page`,
    `complete: bool` and `end_reason: str`.
  - Heading levels per chapter come from heading widths and heights, relative to each other, as `trap._section_end`
    does.
  - Ends: a heading of equal or greater level, or a chapter title (`Chapter N:`, at least 1.4× the median height).
  - Strip sidebars, art credits and running heads using the existing helpers.
- Tests:
  - a GGR guild chapter yields guild sections with parents;
  - a section never crosses a chapter title (Review Focus 1);
  - a sidebar inside a section is excluded.
- Commit: `feat(bookextractor): shared section reader`.

### Task 3: God parser

**Files:** create `bookextractor/parsers/god.py` and `tests/test_god.py`.

- Table rows: `read_rows` on deity tables, with headers Deity, Alignment, Suggested Domains, Symbol. The pantheon
  comes from the table title.
- Prose sections: deity headings followed by printed alignment, domain and symbol lines (MTF, EGW, ERLW, VRGR, SCAG
  prose).
- Checks, each failing with the offending word named:
  - `alignment-known`: an L/N/C × G/N/E grid, or "Unaligned";
  - `domains-known`: from the closed set of every domain the 16 books print, which is built from the class and
    subclass data;
  - `symbol-present`.
- Tests:
  - the spec scenarios: Anubis, and the "Tempset" failure;
  - one prose god from MTF or EGW;
  - Review Focus 5: a god section that hosts a magic item.
- Commit: `feat(bookextractor): god parser`.

### Task 4: Rule parser

**Files:** create `bookextractor/parsers/rule.py` and `tests/test_rule.py`.

- Rules chapters are found by printed chapter titles, never hard-coded page numbers:
  - PHB "Using Ability Scores", "Adventuring", "Combat" and "Spellcasting";
  - the DMG chapters 8 and 9 titles;
  - the XGE and TCE rules chapters.
- A Rule is any section at the chapter's entry level, any "Variant:" or "Optional Rule:" section, and any downtime
  activity.
- `ruleType` is set only from a printed Variant or Optional label.
- Check: `text-complete`.
- Tests:
  - "Customizing Ability Scores" has ruleType V;
  - a downtime activity is found;
  - a bold lead-in stays in its parent (Review Focus 4).
- Commit: `feat(bookextractor): rule parser`.

### Task 5: World section parser (Plane, Faction, Location, Lore)

**Files:** create `bookextractor/parsers/world.py` and `tests/test_world.py`.

- It uses `read_sections` outside the rules chapters and outside sections already owned by other parsers. Phase 1/2a/2b
  entity spans are excluded: pass the claimed line spans through `ctx`, or exclude sections whose heading is an earlier
  entity's name.
- Type keywords, from the nearest enclosing printed heading:
  - Plane: plane, planes, realm, layer, domain of dread;
  - Faction: faction, factions, guild, organization, house, order, cult, company;
  - Location: city, region, kingdom, nation, settlement, location, places, district, island;
  - Lore: everything else.
- Check `type-from-context`: it passes with evidence, and fails when the context holds conflicting words (Review
  Focus 3).
- Fields: `category` from the enclosing heading; `setting` and `settingContext` from the book's setting, if the book
  is a setting book (ERLW, EGW, GGR, SCAG, VRGR, SCC).
- Tests:
  - GGR guild sections are Factions (the spec scenario);
  - an ERLW nation is a Location;
  - a VRGR domain of dread is a Plane or Location, per the context rule;
  - the conflict case.
- Commit: `feat(bookextractor): world section parser`.

### Task 6: Register the six types

**Files:** modify `assemble.py` (`ALL_TYPES`, `_PARSERS`, where `report.TYPES` derives from `ALL_TYPES`) and the
tests.

- No double emission with the earlier types.
- The gate runs over the new types.
- The PHB fixture yields `phb14.god.anubis` and the "Customizing Ability Scores" Rule.
- Run the corpus loop (v33). Earlier-type score lines and dispositions must not change.
- Commit: `feat(bookextractor): register world types`.

### Task 7: Whole-book fix rounds (controller-run)

- Use the corpus loop to reach the done bar. Each fix round runs fixture-first and keeps earlier types identical.
- Residues go in the ledger, with which source is right for each.

### Task 8: App side, final review, live run (controller-run)

- A C# render/load test for one entity of each of the six types.
- The final whole-branch review (most capable model), then one fix wave and a scoped re-review.
- Rebuild, run all 16 books live, check the files equal the local run, verify `dnd_entities` per book, and commit the
  canonical files.
