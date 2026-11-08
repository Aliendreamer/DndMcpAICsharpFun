## Context

Phases 2a/2b (archived 2026-11-06) built the extractor machinery this change reuses: `ParseContext`, `_PARSERS`,
`tables.read_rows` (label-anchored rows), section-end rules (trap/hazard relative heading widths, chapter-title stops),
sidebar/art-credit stripping, vocabulary name repair, the `text-not-shared` gate (one-sided for older types), table-row
attestation, and the corpus loop (`runall.sh`, `scoreall.sh` incl. every book, `accepted_wrong13.py`). The runner purges
stale points per book. Owner decisions carried: D4; never guess names or fields; no LLM/5etools at runtime; one-time
review load; done bar as in the proposal; the umbrella design leaves Lore/Rule/Location typing to the builder.

## Goals / Non-Goals

**Goals:** God, Rule, Plane, Faction, Location, Lore from all 16 books in the existing override schemas, with checks;
scorer truth and text precision (min/median, entities under 0.7 listed) for every type.

**Non-Goals:** adventure-module narrative (no adventures are registered); spell lists, item tables and stat blocks
already covered by other types; MM monster lore (stays inside Monster entries).

## Decisions

1. **God rows and god sections.** Deity tables (header Deity / Alignment / Suggested Domains / Symbol) are read with
   `read_rows`; the pantheon comes from the table title. Prose deity sections (a deity heading followed by printed
   alignment/domain/symbol lines or a statblock-like block) are read where printed. Domains come only from print
   (5etools merges later books' domains into PHB gods — the scorer compares ours ⊆ truth and lists the extra truth
   domains as known differences).
2. **Rule = named rule section.** A section whose heading is printed in a rules chapter (or carries a "Variant:" /
   "Optional Rule:" prefix, or is a downtime activity) becomes a Rule, text heading-bounded; `ruleType` V/O only when
   the print says Variant/Optional. Truth: `variantrules.json`; recall over named variant/optional rules; other rule
   sections are scored against the book section tree.
3. **Section types typed by printed context.** Within setting/lore chapters, each section at the book's "entry" level
   (the heading level whose children are the subject's details — chosen per chapter from heading widths, as trap.py
   does) becomes an entity. Its type comes from the nearest printed enclosing heading: plane words (Plane, Planes,
   Realm, Domain of Dread, Layer) → Plane; organisation words (Faction, Factions, Guild, Organization, House, Order,
   Cult, Company) → Faction; place words (City, Region, Kingdom, Nation, Settlement, Location, Places, Districts,
   Islands) → Location; otherwise Lore. No enclosing word → Lore. The rule is recorded on the entity as a passing
   check (`type-from-context`) naming the heading it used.
4. **Completeness and bleed guards are mandatory** (2a/2b lessons): sections end at equal/greater headings or chapter
   titles; sidebars, art credits and running heads stripped; the text-not-shared gate applies; text precision scored
   per type against the section tree.
5. Fixture-first development, whole-corpus loop after every change; Phase 1/2a/2b score lines and dispositions must
   not change unless explained.

## Risks / Trade-offs

- **Scale** (thousands of sections) → keep the entry level per chapter, not every sub-heading; measure counts per book.
- **Mis-typing** (a guild's district typed Location) → type is recorded with its evidence; a section under two
  conflicting context words fails `type-from-context`.
- **Sections overlapping other types** (a god's section containing a magic item; a faction section with an NPC stat
  block) → the existing monster/vehicle-in-host exemption shape; others fail the gate.

## Migration Plan

Same as 2a/2b: local corpus loop to the done bar → final review → rebuild + live run of 16 books → verify
`dnd_entities` per book → commit → finish routine.

## Open Questions

None blocking.
