## 1. Preserve tables in the converter

- [x] 1.1 `MinerUBlock`: add `table_body` (string?) and `table_caption` (string[]? — join to a caption) properties.
- [x] 1.2 `PdfStructureItem`: add a trailing optional `string? Html = null` (existing constructions unaffected).
- [x] 1.3 `MinerUPdfConverter` block loop: a `table`-typed block (with non-empty `table_body`) → `new PdfStructureItem("table", caption, page, null, Html: table_body)`. Keep image/equation/header/footer dropped. Update the class doc comment (the "tables are dropped" line).

## 2. HtmlTableParser (deterministic)

- [x] 2.1 New `HtmlTableParser.Parse(string html, string tableId, string name, ProvenanceRef cellProvenance) → CanonicalTable?`: split `<tr>…</tr>`; per row split `<t[hd]>…</t[hd]>`; strip inner tags, decode HTML entities, collapse whitespace. First row → `Columns` (fallback `col0..colN` if it is clearly data, not headers); remaining rows → `CanonicalTableRow`(cells with the shared provenance). Minimal rowspan/colspan (repeat/pad). Empty/malformed → null (no throw).
- [x] 2.2 Unit tests: the Draconic Ancestry fixture HTML → columns `[Dragon, Damage Type, Breath Weapon]` + rows (Black/Acid/…, Blue/Lightning/…); whitespace/entity/OCR tolerance; a `<th>`-header table; a header-less table falls back to `col0..`; empty/garbage HTML → null.

## 3. Collect tables into the canonical JSON

- [x] 3.1 In the extraction path that builds `CanonicalJsonFile` from the converted document, add a table-collection step: filter converted items for type `"table"`, parse each via `HtmlTableParser` (id `<bookslug>.table.<slug(caption|index)>`, name = caption, provenance = source book + table page), and set `CanonicalJsonFile.Tables`. Independent of the LLM entity path.
- [x] 3.2 Confirm `CanonicalJsonWriter` serializes `Tables` (it already does for hand-authored files) and no projector change is needed (`StructuredFactProjector` already consumes `CanonicalJsonFile.Tables`).

## 4. Tests

- [x] 4.1 Converter test: a MinerU `content_list` fixture with a `table` block → the converted document has a `table` `PdfStructureItem` with the HTML (not dropped); a non-table decorative block is still dropped.
- [x] 4.2 Collection test: converted document with table item(s) → `CanonicalJsonFile.Tables` populated with the parsed `CanonicalTable`(s), cells carrying `ProvenanceRef` (source book + page).
- [x] 4.3 (Parser tests are 2.2.)

## 5. Verify

- [x] 5.1 `dotnet build` clean (warnings-as-errors) + full `dotnet test` green.
- [ ] 5.2 (Run step — fast, not GPU) Cache-bust + re-convert one book (e.g. PHB) through MinerU, run `extract-entities`, and confirm `CanonicalTable`(s) appear in `books/canonical/<book>.json` `tables[]`; after `ingest-entities`, confirm rows in Postgres `StructuredTables`/`StructuredTableRows`. (Draconic Ancestry breath resolution end-to-end additionally needs a choice-set — documented follow-on.)
