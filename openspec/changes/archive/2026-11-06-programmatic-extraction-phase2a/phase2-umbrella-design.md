# Programmatic extraction — Phase 2 design

Date: 2026-11-06. Follows Phase 1 (`openspec/changes/archive/*programmatic-extraction-phase1/`).

## Intent

The owner's end result: **every book we have is data-mined into correct, well-shaped entities, of every type.**
Phase 1 did Monster, Spell and MagicItem for all 16 registered books; switching a book to the programmatic
extractor dropped every other type the old LLM extraction had produced (~2,000 entities, including the 12 PHB
classes). Phase 2 brings all of them back, extracted deterministically by the same OCR + rules extractor.

The heavy review queue this produces is accepted by the owner as a **one-time cost**: approvals live in
`<slug>.overrides.json` keyed by entity id plus the waived checks, and every re-run re-applies them. Books will
be re-run many times; a reviewed entity comes back to review only if a re-run fails a check that was not waived.

## Goals

- Extract all 20 remaining entity types from all 16 books: Class, Subclass, Race, Subrace, Feat, Background,
  Weapon, Armor, Item, VehicleMount, Object, Trap, DiseasePoison, Condition, God, Plane, Faction, Location,
  Lore, Rule.
- Every type gets **its own fields**, not just a name and text (owner: "we want it all") — e.g. a god's
  alignment, domains and symbol; a class's hit die, proficiencies and features by level.
- Output in the existing `Domain/Entities/Fields/*Fields.cs` shapes (5etools-shaped), so the character coach
  (`Features/CharacterAdvice/*`), retrieval and MCP tools read them unchanged.
- One canonical file per book holds all types; `extract-programmatic` stays the single route.

## Anti-goals (carried from Phase 1, owner decisions)

- A wrong value reaching the chat **unflagged**. An entity is Accepted only if every check passes (D4); any
  failure → NeedsReview.
- Guessed names. A name is a heading as printed in the book, or the entity is not emitted.
- No LLM, no 5etools data at runtime. 5etools is a **development-time scoring truth only**.
- No staging (D9).

## Constraints

- Python extractor `Tools/BookExtractor`, RapidOCR on CPU, OCR cache reused (no re-OCR needed; all 16 books
  are cached). Never GPU.
- C# side: Serena for edits; endpoint changes update `.http` + Insomnia; markdown lints clean; work on main.
- Phase 1 scores must not regress (all-book score diff + wrong-and-accepted audit after every parser change).

## Shape of the work — three changes, in this order

Each is its own openspec change, brainstorm-light (this document covers intent), plan, SDD, live re-run of all
16 books, finish routine.

1. **2a — character data:** Class, Subclass, Race, Subrace, Feat, Background. First because the character coach
   (level-up, build recommender, build critique) reads these directly and is currently running without them.
2. **2b — equipment and hazards:** Weapon, Armor, Item (adventuring gear, tools, packs), VehicleMount, Object,
   Trap, DiseasePoison, Condition. Mostly tables and short fixed blocks.
3. **2c — world:** God, Plane, Faction, Location, Lore, Rule. Prose sections with fields pulled out of them.

The order is the builder's (my) call; the owner did not object.

## What "correct" means per type

General, every type: name exactly as printed; complete text from heading to the next heading of the same or
higher level (no truncation, no bleed into the next entity, no page furniture); correct page; correct type.

Self-checks (the D4 gate). Each check is named, has a failure reason, and is unit-tested like Phase 1's
`hp-dice-matches-hp`. Starting set — the builder adds more where the book offers arithmetic or a closed set:

| Type | Checks |
| --- | --- |
| Class | hit die agrees across "Hit Dice: 1dN per level", "Hit Points at 1st Level: N + CON" and "Higher Levels"; level table has 20 rows with proficiency bonus +2…+6 in the standard steps; every feature named in the table has a matching feature heading in the text and vice-versa; spell-slot columns match full/half/third-caster progression if present |
| Subclass | parent class resolves to a known class name; its feature levels are exactly the class's subclass-feature levels |
| Race / Subrace | ability score increase parses to abilities + amounts; size in the closed set; speed a multiple of 5; subrace's parent race resolves |
| Feat | prerequisite (if any) parses; text non-empty and complete |
| Background | exactly the printed skill proficiencies, each a real skill; feature present |
| Weapon / Armor / Item | table row complete: cost with a valid unit, weight, damage dice valid, properties from the closed set; AC formula valid |
| Trap / DiseasePoison | DCs and damage dice parse; poison type in the closed set |
| Condition | name in the closed set of conditions |
| God | alignment parses to a real alignment; domains from the closed domain list; symbol present when the book prints one |
| Plane / Faction / Location / Lore / Rule | heading-bounded section: text complete (ends at next heading of same or higher level), no stat block or sidebar swallowed |

## Scoring (development only)

Extend `bookextractor score` with the 5etools truth for each new type: `class/`, `races.json`, `feats.json`,
`backgrounds.json`, `items-base.json`, `vehicles.json`, `objects.json`, `trapshazards.json`,
`conditionsdiseases.json`, `deities.json`, `variantrules.json`, and — for Lore/Rule/Location/Faction/Plane —
the per-book section tree `5etools/book/book-<key>.json` (headings + text). Same per-book score table and
wrong-and-accepted audit as Phase 1.

## Done bar (owner-approved, 2026-11-06)

A change (2a, 2b, 2c) ships when, across all 16 books, for its types:

- **zero unexplained wrong-and-accepted entities** (each residue named, with which source is right), and
- **≥ 90% of each book's 5etools entries for those types are found** (books without 5etools truth for a type
  are judged by the section tree / spot check).

## Decisions from the builder check (my calls, not the owner's — flagged for review)

1. **Class features stay inside the class/subclass**, no new entity type. The extractor synthesizes the 5etools
   ref strings the coach parses (`Name|Class|Source|Level`, subclass 6-part form) from the book's level table and
   feature headings; the feature text goes in `entries`. `SubclassTitle` is the printed heading ("Martial
   Archetypes" → singular as 5etools has it). A subclass's parent-class source is the 5etools key of the book
   that defines the class (XGE subclasses of PHB classes → `PHB`).
2. **5etools shape is required only for fields a C# consumer reads** (coach, renderers, retrieval). Those are
   scored field-by-field against 5etools. Free-form `JsonElement` fields nobody reads may be a looser parse.
3. **Fields the book does not print are null, never guessed.** A missing optional field is not a check failure;
   a printed-but-unparsable field is.
4. **A table that fails its checks ships as NeedsReview with the partial data**, so each change can ship
   before table reading is perfect. The fix-round loop then drives the review count down.
5. **Reprints are separate entities per book**, ids by book slug as in Phase 1 (`mpmm.race.goliath` and
   `vgm`-era copies are distinct). The SRD is not registered and is out of scope.
6. **Downstream C# is in scope per change:** each change confirms every type it adds renders canonical text,
   ingests, and shows its fields on `/review`; any gap is a task in that change's plan.
7. **Item boundary:** mundane gear, tools and packs → Item; weapons → Weapon; armor → Armor; anything magical
   (including +1 variants) stays MagicItem from Phase 1.
8. **Ids are stable** (`<slug>.<type>.<name-slug>`, as Phase 1), so approvals keep matching across re-runs; Phase 1's
   orphaned-override count in the run log already reports any that stop matching.

## Builder's calls (left open)

- Parser module layout, layout/reading-order changes, table reading approach.
- Exact id scheme follows Phase 1 (`<slug>.<type>.<name-slug>`).
- What counts as Lore vs Rule vs Location in a given chapter (decide from the book section tree; document the
  rule).
- Whether the review UI needs per-type rendering for the new field shapes (it must at least show them).
