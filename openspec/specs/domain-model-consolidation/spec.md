# domain-model-consolidation Specification

## Purpose
TBD - created by archiving change merge-companion-into-main. Update Purpose after archive.
## Requirements
### Requirement: All domain model types live in one Domain folder
The system SHALL define every domain model type (`User`, `Campaign`, `Hero`, `HeroSnapshot`, `CharacterSheet`, chat message types, `IngestionRecord`, and the existing entity/book types) under the single `Domain/` folder. Repositories, services, and UI components SHALL NOT define their own domain model types.

#### Scenario: Models centralized
- **WHEN** the codebase is inspected for domain record/entity definitions
- **THEN** they are found under `Domain/` and not redefined inside feature folders or repository files

#### Scenario: IngestionRecord relocated
- **WHEN** locating the `IngestionRecord` type
- **THEN** it resides in `Domain/`, not in an infrastructure/persistence folder

### Requirement: Domain types are independent of persistence and UI
Domain model types SHALL NOT depend on EF Core, ADO.NET, Blazor, or framework infrastructure types. Persistence mapping SHALL be configured in `AppDbContext`, not via attributes that couple domain types to a specific provider.

#### Scenario: Domain compiles without persistence references
- **WHEN** a domain model type is examined
- **THEN** it carries no EF Core mapping attributes or data-access dependencies; mapping lives in the context configuration

