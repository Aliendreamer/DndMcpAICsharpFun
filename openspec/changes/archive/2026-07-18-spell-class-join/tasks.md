## 1. SpellClassIndex

- [x] 1.1 Add `SpellClassIndex` (singleton) that lazily loads `5etools/spells/sources.json` (dir from config, default the 5etools data dir). Parse `{ SOURCE: { SpellName: { class:[{name}], classVariant:[{name}] } } }` → `Dictionary<(normName, normSource), HashSet<string>>` (+ a source-agnostic `Dictionary<normName, HashSet<string>>` fallback). Normalize = lowercase alphanumerics. Missing file → empty index, no throw.
- [x] 1.2 Expose `ClassesFor(string name, string? source)` and `CanCast(string className, string name, string? source)` (source-specific match first, name-only fallback; class name compared normalized).

## 2. Service — castable-by-class branch

- [x] 2.1 Add `CastableByClass` (string?) to `EntitySearchQuery`.
- [x] 2.2 In `EntityRetrievalService.ListAsync`: when `CastableByClass` is set, force type=Spell, call `ListByFilterAsync(filters, cap: MaxScan)` for the full payload-filtered spell set, filter in-memory by `SpellClassIndex.CanCast`, set `Total` = filtered count, `Rows` = first `min(limit, filtered)` compact `EntitySetRow`s. Log if MaxScan is ever hit.

## 3. MCP tool + endpoint

- [x] 3.1 Add `castableByClass` param to `DndMcpTools.list_entities` (Description: "only spells this class can learn, e.g. Wizard; combine with spellLevel/school").
- [x] 3.2 Add `castableByClass` query param to `GET /retrieval/entities/list` (`ListPublic` → `EntitySearchQuery.CastableByClass`).
- [x] 3.3 Update `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json` with a castableByClass example (contract rule, same commit).

## 4. DI

- [x] 4.1 Register `SpellClassIndex` as a singleton (embeds/loads `sources.json` once).

## 5. Tests

- [x] 5.1 `SpellClassIndex` test over a small fixture `sources.json`: Fireball → {Sorcerer, Wizard}; name/punctuation normalization matches; unknown spell → empty; missing file → empty (no throw).
- [x] 5.2 Service test: fake store returns a spell set + a `SpellClassIndex` (fixture) → `castableByClass="Wizard"` returns only Wizard-castable spells, `Total` = class-filtered count, truncation honored.
- [x] 5.3 MCP-tool test: `list_entities(castableByClass:"Wizard")` → compact JSON `{total,returned,rows}` of only Wizard spells.

## 6. Verify

- [x] 6.1 `dotnet build` clean (warnings-as-errors) + full `dotnet test` green.
- [ ] 6.2 (Optional, manual) Live: `GET /retrieval/entities/list?castableByClass=Wizard&spellLevel=3` returns the complete set of level-3 Wizard spells with an honest total; a chat "which level-3 spells can a Wizard learn" routes to structured-lookup and calls `list_entities`. Not required — no data migration; the query reads sources.json on disk.
