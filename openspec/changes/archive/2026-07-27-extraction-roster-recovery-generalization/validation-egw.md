# Live validation — EGW re-extract (2026-07-26)

Covers BOTH `extraction-statline-name-repair` and `extraction-roster-recovery-generalization`; one
`force=true` re-extract of book 16 validates the pair, since they were designed to land together.

Baseline: the pre-fix run, saved before `force=true` overwrote it (also kept by the run itself as
`books/canonical/egw.json.bak`).

## Headline

| | Before | After |
| --- | --- | --- |
| Entities | 82 | **100** |
| Candidates | 101 | **124** |
| Recovered candidates | 10 (Monster only) | **48** (8 types) |
| Run time | 66 min | 135 min |

Type histogram:

| Type | Before | After |
| --- | --- | --- |
| Monster | 32 | 29 |
| MagicItem | 24 | 24 |
| Rule | 12 | 5 |
| Item | 7 | 2 |
| Lore | 6 | 5 |
| God | 1 | 6 |
| Spell | 0 | **14** |
| Race | 0 | **7** |
| Subclass | 0 | **4** |
| Background | 0 | **2** |
| Class | 0 | 2 |

## Verdict per change

**statline-name-repair — confirmed.** 17 of the 19 removed entities are stat-line names
(`Wondrous item, very rare`, `Weapon (greataxe), very rare (requires attunement)`, `Potion, rare`, …).
The `Rule` bucket fell 12 → 5 and `Item` 7 → 2, which is where that junk lived. Two removals are the
*repair* working rather than a deletion — each reappears clean and correctly typed:

- `AMULET OF THE DRUNKARD Wondrous item, uncommon` (Rule) → `Amulet of the Drunkard` (MagicItem)
- `RING OF TEMPORAL SALVATION Ring, rare (requires attunement)` (Item) → `Ring of Temporal Salvation` (MagicItem)

No real magic item was lost: `MagicItem` held at 24 while gaining those two, because the junk that had
been mis-filed as MagicItem left.

**roster-recovery-generalization — confirmed.** All 37 added entities are real EGW content that was
previously dropped by the TOC gate: 14 dunamancy spells (`Gravity Sinkhole`, `Ravenous Void`,
`Time Ravage`, `Temporal Shunt`, …), the 3 target subclasses (`Echo Knight`, `Chronurgy Magic`,
`Graviturgy Magic`) plus `War Domain`, 7 races, 6 Exandrian gods, and the `Grinner` /
`Volstrucker Agent` backgrounds. Recovery fired for 8 distinct types where it previously fired for one.

Mis-typing fix: `Orc` moved `Monster → Race` — EGW's own Orc *is* an EGW-sourced Race entry, so
same-source preference resolved it.

## Known gaps (not regressions — carried forward)

**Three reprinted races remain typed `Monster`: `Sea Elf`, `Aarakocra`, `Kenku`.** 5etools attributes
these to other sources, so same-source preference finds nothing and the global fallback returns the
bestiary Monster entry. The confusion-set fix (`38cb2c8`) *does* put `Race` in their prior — the model
simply did not take it. This is the qwen3:8b grounding ceiling, not a wiring fault: the mechanism is
present and demonstrably works when the roster agrees (`Orc`). Fix by hand-correcting the three types
during the gap-fill pass; the 5etools backfill will not repair them (it would add parallel `Race`
entities and leave the `Monster` ones behind as duplicates).

**Two entities lost to transient LLM failures**, both recorded in `egw.errors.json` as
`extraction_failure: Empty response from Ollama` — unrelated to either change:

- `Tortle` (was Monster; is actually an EGW race)
- `Aeorian Hunters` (was Lore)

Recoverable with `POST /admin/books/16/extract-entities?errorsOnly=true`, which is the planned next step.

**One unexplained loss: `Core Spawn Crawler` (Monster).** Absent from the new canonical, absent from
`egw.errors.json`, and EGW produced no `egw.declined.json` at all this run. Candidate accounting also
does not close: 124 candidates vs 100 entities + 12 errors = 112, leaving 12 unaccounted. This is the
traceless-drop class the dev-flow warns about and it deserves a follow-up — a candidate that vanishes
with no entity, no decline and no error should log at the silent path. Not caused by these changes
(the pre-fix run had the same accounting shape), but now visible.

**Subraces were indexed but not recovered.** `Draconblood`, `Ravenite`, `Pallid` and `Lotusden` are now
in the index (they were not before), yet no `Subrace` candidate was recovered — their headings do not
appear as standalone sections in the MinerU output, so there is nothing for the matcher to match. This
is an upstream parser gap, not a gate failure, and stays on the hand-authoring list.
