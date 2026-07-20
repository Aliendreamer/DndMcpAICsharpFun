## Context
`MinerUTableCollector` names each `CanonicalTable` from the table item's caption (`table_caption`, in `item.Text`), falling back to `Table {index}`. MinerU only captions ~11% of tables; the rest have their name in the preceding `section_header`.

## Goals / Non-Goals
**Goals:** meaningful table names/ids from the preceding heading when MinerU gives no caption. **Non-Goals:** changing MinerU; parsing; the resolution-engine id-alignment (separate follow-on).

## Decisions
- **D1** — the collector already iterates `doc.Items` in order; track the last `section_header`; use it as the name when the table caption is empty, bounded by a look-back window so a distant heading isn't misattributed. **D2** — id slug derives from the resolved name; keep the positional `-<n>` suffix for uniqueness.

## Risks / Trade-offs
- A table under a broad section grabs the section name (e.g. "Constitution") rather than a precise caption — still far better than `Table N`, and correct for D&D's heading-per-table layout. Mitigation: bounded window.
