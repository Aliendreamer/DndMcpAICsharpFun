## ADDED Requirements

### Requirement: OllamaHealthCheck reports Healthy when Ollama responds
`OllamaHealthCheck.CheckHealthAsync` SHALL return `HealthCheckResult.Healthy()` when `ListLocalModelsAsync` completes without throwing.

#### Scenario: ListLocalModelsAsync succeeds
- **WHEN** `CheckHealthAsync` is called and `ListLocalModelsAsync` returns successfully
- **THEN** the result status is `HealthStatus.Healthy`

### Requirement: OllamaHealthCheck reports Unhealthy when Ollama is unreachable
`OllamaHealthCheck.CheckHealthAsync` SHALL return `HealthCheckResult.Unhealthy` with description "Ollama is unreachable" when `ListLocalModelsAsync` throws any exception.

#### Scenario: ListLocalModelsAsync throws exception
- **WHEN** `CheckHealthAsync` is called and `ListLocalModelsAsync` throws an `HttpRequestException`
- **THEN** the result status is `HealthStatus.Unhealthy`
- **AND** the result description is "Ollama is unreachable"
- **AND** the thrown exception is attached to the result
