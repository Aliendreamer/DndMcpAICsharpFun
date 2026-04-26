## ADDED Requirements

### Requirement: start.sh launches the Docker stack with environment selection
The system SHALL provide an executable `start.sh` at the project root that accepts exactly one argument (`Development` or `Production`), exports it as `ASPNETCORE_ENVIRONMENT`, and runs `docker compose up --build -d`.

#### Scenario: Script launches stack in Development mode
- **WHEN** `./start.sh Development` is executed
- **THEN** `docker compose up --build -d` runs with `ASPNETCORE_ENVIRONMENT=Development` exported to the environment

#### Scenario: Script launches stack in Production mode
- **WHEN** `./start.sh Production` is executed
- **THEN** `docker compose up --build -d` runs with `ASPNETCORE_ENVIRONMENT=Production` exported to the environment

#### Scenario: Script fails with no argument
- **WHEN** `./start.sh` is called with no arguments
- **THEN** the script exits non-zero and prints usage instructions

#### Scenario: Script fails with invalid argument
- **WHEN** `./start.sh staging` or any value other than `Development` or `Production` is passed
- **THEN** the script exits non-zero and prints an error message listing valid values
