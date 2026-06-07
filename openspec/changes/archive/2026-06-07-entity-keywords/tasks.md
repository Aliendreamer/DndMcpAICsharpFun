## 1. EntityEnvelope — Add Keywords Field

- [x] 1.1 Add `IReadOnlyList<string> Keywords = []` property to `EntityEnvelope` record (`Domain/Entities/EntityEnvelope.cs`) with an empty-list default

## 2. Qdrant — Write and Read Keywords Payload

- [x] 2.1 In `QdrantEntityVectorStore.ToPoint`: write `Keywords` to the Qdrant payload as a repeated keyword value (one payload entry per keyword string, using `EntityPayloadFields.Keywords`)
- [x] 2.2 In `QdrantEntityVectorStore.ToEnvelope`: read `keywords` payload back into `EntityEnvelope.Keywords` (split repeated keyword values into a list)
- [x] 2.3 Verify the keyword filter in `BuildMustConditions` already uses `EntityPayloadFields.Keywords` and `EntityFilters.Keyword` — no change needed if correct

## 3. 5etools Monster Mapper — Wire traitTags

- [x] 3.1 In `FivetoolsMonsterMapper` (or `FivetoolsMapperBase.Map`): read `traitTags` array from the 5etools JSON element and map it to `Keywords` on the `EntityEnvelope`
- [x] 3.2 All other 5etools mapper types (Spell, Class, Item, etc.) leave `Keywords` at the default empty list — confirm no change needed

## 4. Canonical JSON Loader — Read keywords from fields

- [x] 4.1 In `CanonicalJsonLoader` (or the envelope deserialization path): when loading an entity from canonical JSON, read `fields.keywords` (string array, optional) and populate `EntityEnvelope.Keywords`

## 5. Extraction Schema and System Prompt — LLM keywords guidance

- [x] 5.1 Add `"keywords"` as an optional `string[]` property to `Schemas/canonical/MonsterFields.schema.json` with description `"Notable trait names from the stat block (e.g. Pack Tactics, Amphibious)"`
- [x] 5.2 Add a brief instruction to the Monster extraction system prompt: ask the LLM to populate `keywords` with an array of trait/feature names visible in the stat block (keep names as they appear in the text)

## 6. HTTP Contracts — Update examples

- [x] 6.1 Update `DndMcpAICsharpFun.http`: change the monster CR search example to use a keyword that actually exists (e.g. `keyword=Amphibious`) and add a comment that keywords come from trait tags
- [x] 6.2 Update `dnd-mcp-api.insomnia.json`: update `req_retrieval_entities_monster` parameters to use `keyword=Amphibious`

## 7. Re-ingest 5etools

- [x] 7.1 After shipping the code, run `POST /admin/5etools/import` to re-populate `dnd_entities` with keywords for all 5etools monsters
- [x] 7.2 Verify with `GET /retrieval/entities/search?q=frog&type=Monster&keyword=Amphibious` that results include Giant Frog and similar amphibious monsters
