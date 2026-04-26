## ADDED Requirements

### Requirement: CLAUDE.md documents the API Contracts convention
The system SHALL include an "API Contracts" section in `CLAUDE.md` that instructs Claude Code to update `DndMcpAICsharpFun.http` whenever any endpoint is added, changed, or removed.

#### Scenario: Convention is discoverable by Claude Code
- **WHEN** Claude Code starts a session in this repository
- **THEN** it reads the API Contracts rule from `CLAUDE.md` and applies it to any task involving endpoint changes

#### Scenario: Rule covers all HTTP method types
- **WHEN** a developer or AI adds a route using `MapGet`, `MapPost`, `MapPut`, or `MapDelete`
- **THEN** the CLAUDE.md rule requires a corresponding update to `DndMcpAICsharpFun.http` in the same commit
