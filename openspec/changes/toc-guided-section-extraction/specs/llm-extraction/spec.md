# llm-extraction (delta)

## ADDED Requirements

### Requirement: Extraction prompt includes TOC-derived entity context
`ILlmEntityExtractor.ExtractAsync` SHALL accept `entityName` (string) and `pageRange` (startPage, endPage ints) parameters in addition to existing parameters. The system prompt SHALL include: "This is a section from the {entityName} {entityType} (pages {startPage}–{endPage})."

#### Scenario: Context hint appears in prompt
- **WHEN** `ExtractAsync` is called with entityName="Warlock", startPage=105, endPage=112
- **THEN** the LLM request contains "Warlock", "105", and "112" in the system prompt

#### Scenario: Context hint reduces wrong-shape JSON
- **WHEN** the model receives an explicit entity name and category
- **THEN** extraction returns a JSON array (not a keyed object) because the model no longer needs to infer structure from headings

### Requirement: Trait and Lore are valid extraction types
`ILlmEntityExtractor` SHALL handle `entityType = "Trait"` and `entityType = "Lore"` with appropriate field schemas. Trait fields: `description (string), source_category (string)`. Lore fields: `description (string)`.

#### Scenario: Trait extraction returns valid entity
- **WHEN** `ExtractAsync` is called with entityType="Trait"
- **THEN** the returned entities have `data.description` populated

#### Scenario: Lore extraction returns valid entity
- **WHEN** `ExtractAsync` is called with entityType="Lore"
- **THEN** the returned entities have `data.description` populated
