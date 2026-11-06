## Context

Phase 2a (`openspec/changes/archive/2026-11-06-programmatic-extraction-phase2a/`, including the Phase 2 umbrella
design `phase2-umbrella-design.md`) added the character types to `Tools/BookExtractor`. The pieces it built and this
change reuses:

- `ParseContext` and the registry of parsers in `assemble._PARSERS`;
- `tables.read_table`, the class level-table reader;
- `charcommon` helpers;
- feature-name vocabulary repair;
- the `text-not-shared` gate;
- the corpus loop: `runall.sh vN`, `scoreall.sh vN` and `accepted_wrong13.py`.

All 16 books run `extract-programmatic` live. The runner purges a book's stale points after each run.

Owner decisions carried over:

- **D4:** an entity is Accepted only if every check passes.
- Never guess a name or a field.
- No LLM, and no 5etools data at runtime.
- The review load is a one-time cost.
- The done bar is at least 90% of 5etools entries found per book and type, with zero unexplained wrong-and-accepted
  values.

## Goals / Non-Goals

**Goals:**

- Extract Weapon, Armor, Item, VehicleMount, Object, Trap, DiseasePoison and Condition from all 16 books, in the
  existing field shapes, each with its own self-checks.
- Read table rows as entities, joined to their description paragraph where the book prints one.
- Add scorer truth, text precision and an audit for these types.

**Non-Goals:**

- World types (Phase 2c).
- Magic items, which Phase 1 covers.
- Services, lifestyle expenses and food or lodging prices. These are rules tables, so they come with Rule in 2c.
- Spell components.

## Decisions

1. **Table rows are entities.**
   - PHB weapons, armor, adventuring gear, tools, mounts and vehicles, and trade goods are table rows. Each row
     becomes one entity.
   - Its name is the printed row label. Its fields come from the printed cells.
   - Its `entries` come from the matching description paragraph, a heading or bold lead-in of the same name, when
     one exists.
   - The table reader is generalised: rows anchor on the first column's label instead of a level ordinal. Header
     spans give the columns, and grouped sub-headings ("Simple Melee Weapons", "Light Armor") give the category.
   - Alternative rejected: description paragraphs only. Most cost, weight and damage values exist only in the
     tables.
2. **Item boundary.**
   - Weapon covers anything printed in a weapons table, including the DMG's Renaissance and Modern weapons.
   - Armor covers armor and shields.
   - Item covers adventuring gear, tools, packs, mounts, tack, vehicles priced as goods, and trade goods.
   - VehicleMount covers vehicles with a stat block or statistics table: the DMG vehicles table and MTF's infernal
     war machines.
   - Object covers siege equipment and the DMG and TCE object stat blocks.
   - Trap covers traps and hazards (`trapHazType`).
   - DiseasePoison covers the diseases and the poisons table rows, with the poison type.
   - Condition covers the PHB Appendix A conditions.
   - Magic items stay MagicItem.
3. **Checks are closed sets and arithmetic.** Examples:
   - cost is `<n> <cp|sp|ep|gp|pp>` (or "—");
   - weight is `<n> lb.`;
   - damage dice match `^\d+d\d+$` or a flat number, with a damage type from the closed set;
   - properties come from the PHB property list;
   - armor AC is `\d+` or `\d+ + Dex modifier (max 2)`;
   - Strength is `Str \d+`;
   - Stealth is "Disadvantage";
   - trap DCs are numbers;
   - poison type is one of contact, ingested, inhaled or injury;
   - condition names come from the 15 PHB conditions.
4. **Fields the book doesn't print stay absent**, the same rule as 2a.
5. **Fixture-first development, then the whole-corpus loop.** Phase 1 and 2a lines must not move, and the
   wrong-and-accepted audit and text precision are checked for every type. The final whole-change review runs before
   the live run, and `dnd_entities` is verified per book after it.

## Risks / Trade-offs

- **Wide tables with grouped rows and footnote markers** (PHB gear has an asterisk on some rows). Mitigation: the
  category comes from the sub-heading, footnote markers are stripped and recorded, and an unreadable row goes to
  NeedsReview.
- **One name shared by a row and an unrelated heading** (e.g. "Net" the weapon, or a "Poisoner's Kit" sidebar).
  Mitigation: a row binds only to a description in the same chapter section, and the text-not-shared gate catches
  bleed.
- **Gear descriptions listed alphabetically apart from the table.** Mitigation: join by exact name inside the
  equipment chapter. With no match, `entries` stays absent.

## Migration Plan

Same as 2a:

1. Local corpus loop until the done bar is met.
2. Final review.
3. Rebuild the containers and run all 16 books live.
4. Verify `dnd_entities` per book.
5. Commit the canonical files.
6. Finish routine.

## Open Questions

None blocking.
