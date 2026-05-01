## ADDED Requirements

### Requirement: Qdrant infrastructure classes are excluded from coverage
Qdrant classes SHALL have `[ExcludeFromCodeCoverage]` applied so they do not lower the reported coverage percentage.

#### Scenario: Qdrant classes carry the exclusion attribute
- **WHEN** coverage is collected
- **THEN** `QdrantVectorStoreService`, `QdrantSearchClientAdapter`, `QdrantCollectionInitializer`, and `QdrantHealthCheck` are not counted

### Requirement: DI-wiring and option classes are excluded from coverage
Extension and option classes that contain only DI registration or configuration record mappings SHALL have `[ExcludeFromCodeCoverage]` applied.

#### Scenario: DI-wiring classes carry the exclusion attribute
- **WHEN** coverage is collected
- **THEN** `ServiceCollectionExtensions`, `WebApplicationExtensions`, `OpenTelemetryOptions`, and `RegisterBookRequest` are not counted

### Requirement: Program.cs is excluded from coverage via ExcludeByFile
The test project SHALL configure coverlet to exclude `Program.cs` using the `ExcludeByFile` MSBuild property so top-level statements are not counted.

#### Scenario: Program.cs does not appear in coverage report
- **WHEN** coverage is collected
- **THEN** `Program` class does not appear in the report
