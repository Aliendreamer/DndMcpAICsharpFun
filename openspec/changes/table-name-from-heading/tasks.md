# Tasks — table-name-from-heading (CAPTURE-ONLY; implement later)

## 1. Name from preceding heading
- [ ] 1.1 In `MinerUTableCollector.Collect`, track the most recent `section_header` text as it iterates `doc.Items`; when a `table` item has an empty caption (`item.Text`), use that heading as the table name (and id slug). Look-back window (e.g. the immediately preceding heading, or within N items). Fall back to `Table N` only when no heading exists.
- [ ] 1.2 Guard: skip over intervening `text` items but bound the window so a table far from any heading doesn't grab an unrelated one.

## 2. Tests
- [ ] 2.1 A table with empty caption preceded by section_header "Draconic Ancestry" → CanonicalTable.Name = "Draconic Ancestry", id `<slug>.table.draconic-ancestry-<n>`.
- [ ] 2.2 A table with a real MinerU caption keeps it (unchanged). A table with no preceding heading → `Table N` fallback.

## 3. Verify
- [ ] 3.1 Build clean + suite green.
- [ ] 3.2 (Optional) Re-extract PHB and confirm the Draconic Ancestry table is named, not "Table 7".
