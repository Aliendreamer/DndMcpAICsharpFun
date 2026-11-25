## ADDED Requirements

### Requirement: Searches made by the app MUST be visible in the eval

The turn trace MUST mark tool calls the chat made itself. A full-chain report MUST record per run whether the app searched, and the summary MUST print the number of such runs. Such a call counts as a called tool for attribution. Reports without the mark MUST still load as baselines.

#### Scenario: The app searched

- **WHEN** a run's trace holds a tool call made by the app
- **THEN** the run's detail says so and the summary counts it
