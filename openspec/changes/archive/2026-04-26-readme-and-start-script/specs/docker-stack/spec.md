## MODIFIED Requirements

### Requirement: Admin API key is injected via environment variable
The system SHALL read the admin API key from the encrypted `Config/appsettings.Production.json` file loaded automatically by the ASP.NET Core host at runtime. The `Admin__ApiKey` environment variable SHALL NOT be defined in `docker-compose.yml`.

#### Scenario: Key is loaded from encrypted config
- **WHEN** the `app` container starts with git-crypt-decrypted config files present
- **THEN** the app reads `Admin:ApiKey` from `Config/appsettings.Production.json` without any environment variable override

## ADDED Requirements

### Requirement: ASPNETCORE_ENVIRONMENT is sourced dynamically from the shell
The system SHALL configure the `app` service in `docker-compose.yml` to read `ASPNETCORE_ENVIRONMENT` from the host shell environment via `${ASPNETCORE_ENVIRONMENT}`, allowing the value to be controlled by `start.sh` without hardcoding.

#### Scenario: Development environment is set via start.sh
- **WHEN** `./start.sh Development` is run
- **THEN** the `app` container receives `ASPNETCORE_ENVIRONMENT=Development` and loads `Config/appsettings.Development.json`

#### Scenario: Production environment is set via start.sh
- **WHEN** `./start.sh Production` is run
- **THEN** the `app` container receives `ASPNETCORE_ENVIRONMENT=Production` and loads `Config/appsettings.Production.json`
