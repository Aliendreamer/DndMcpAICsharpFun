## MODIFIED Requirements

### Requirement: The LLM extraction step uses TOC-guided category filtering
The system SHALL perform TOC bookmark classification as the first step of `ExtractBookAsync`, then dispatch at most one LLM extractor pass per page based on the resulting page-range map, instead of running all categories on every page.

#### Scenario: Extraction runs only the mapped category per page
- **WHEN** `ExtractBookAsync` is called and the PDF has a valid bookmark outline
- **THEN** each page is processed with at most one category extractor, determined by the TOC map

#### Scenario: Extraction falls back to all-categories when no bookmarks are present
- **WHEN** `ExtractBookAsync` is called and the PDF has no embedded bookmark outline
- **THEN** all category extractors run for every page, matching the previous behaviour
