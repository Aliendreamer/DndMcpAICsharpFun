## ADDED Requirements

### Requirement: A parser candidate SHALL be evaluated with deterministic LLM-free A/B evidence before any production swap

A candidate PDF parser SHALL be evaluated against the incumbent by running both parsers' `PdfStructureDocument` outputs for the SAME real book through the existing deterministic downstream paths (`EntityCandidateBuilder`, `MinerUTableCollector`) and comparing: total candidates, recall of the known parser-dropped entities, table count, table degenerate rate, heading-named table share, and section-header counts. The evaluation SHALL produce a written go/no-go against pre-committed criteria; no production converter change happens inside the evaluation.

#### Scenario: A/B comparison on the real book

- **WHEN** the candidate parser's output for the PHB is adapted to a `PdfStructureDocument` and both it and MinerU's cached document are scored through the deterministic paths
- **THEN** a comparison report exists with per-parser numbers for candidates, known-dropped-entity recall, tables (total/degenerate/named), and headings — and a go/no-go decision recorded against the pre-committed criteria

#### Scenario: Evaluation cannot silently become adoption

- **WHEN** the spike concludes (GO or NO-GO)
- **THEN** production (`IPdfStructureConverter`, the conversion cache, extraction endpoints) is unchanged — adoption is a separate follow-up change
