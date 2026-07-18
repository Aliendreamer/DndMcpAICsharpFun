## 1. Preserve tables in the converter

- [x] 1.1 `MinerUBlock`: add `table_body` (string?) and `table_caption` (string[]? — join to a caption) properties.
- [x] 1.2 `PdfStructureItem`: add a trailing optional `string? Html = null` (existing constructions unaffected).
- [x] 1.3 `MinerUPdfConverter` block loop: a `table`-typed block (with non-empty `table_body`) → `new PdfStructureItem("table", caption, page, null, Html: table_body)` — handled BEFORE the empty-text skip (a table's text is empty). Keep image/equation/header/footer dropped. Class doc updated.
- [x] 1.4 **(added — the actual gap)** `MinerUOptions.TableEnable` (default true) + the converter sends `table_enable=true`. MinerU's table recognition is OFF by default, so grid tables came back as text/image. **VERIFIED LIVE:** with `table_enable=true` MinerU emits the PHB Draconic Ancestry table as a `table` block with clean `table_body` = `<table><tr><td>Dragon</td><td>Damage Type</td><td>Breath Weapon</td></tr><tr><td>Black</td><td>Acid</td>…` — the exact shape `HtmlTableParser` parses. (EEPC showed 0 tables only because it lacks clean grid tables; the config was never broken — our converter just wasn't asking.)

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
- [x] 5.2 Live proof — **RESOLVED (no infra change).** Initial EEPC probe showed 0 tables, but that's because EEPC lacks clean grid tables. Probing the **PHB Draconic Ancestry pages** (extracted to a 10-page PDF) with `table_enable=true` (both `ocr` and `auto`) MinerU returned `TABLES: 1` with clean `table_body` HTML — proving MinerU works and the only gap was our converter not requesting table recognition (fixed, 1.4). The link-by-link chain is now verified: MinerU emits the table (live) → `HtmlTableParser` parses that exact HTML (unit) → `MinerUTableCollector` → `CanonicalJsonFile.Tables` (unit) → existing `StructuredFactProjector`/`CharacterResolutionService`. **Remaining run step (fast, no GPU/infra):** rebuild the app + cache-bust one book (delete its `.mineru.json`) + re-run `extract-entities` to populate real `CanonicalTable`s into the canonical JSON + Postgres. (Full breath-weapon resolution also needs a choice-set — documented follow-on.) NOTE: precedence per direction — MinerU (from the book) is PRIMARY; 5etools is the FALLBACK for official books when the book's table is missing/poor (a follow-on).
