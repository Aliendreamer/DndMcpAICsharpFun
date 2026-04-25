# admin-security

## Purpose

Defines the security requirements for protecting administrative routes via API key authentication.

## Requirements

### Requirement: All /admin/* routes require a valid API key
The system SHALL reject requests to any route under `/admin/` that do not supply a valid `X-Admin-Api-Key` header.

#### Scenario: Valid key is accepted
- **WHEN** a request to `/admin/*` includes the correct `X-Admin-Api-Key` header value
- **THEN** the request proceeds to the handler and returns the expected response

#### Scenario: Missing key is rejected
- **WHEN** a request to `/admin/*` omits the `X-Admin-Api-Key` header
- **THEN** the system returns HTTP 401 Unauthorized

#### Scenario: Wrong key is rejected
- **WHEN** a request to `/admin/*` includes an incorrect `X-Admin-Api-Key` value
- **THEN** the system returns HTTP 401 Unauthorized

### Requirement: Non-admin routes are unaffected by admin key middleware
The system SHALL NOT apply the API key check to any route outside `/admin/`.

#### Scenario: Health endpoint requires no key
- **WHEN** a request is made to `GET /health/ready` without any API key header
- **THEN** the system returns a health response without requiring authentication

### Requirement: Admin API key is loaded from configuration
The system SHALL read the expected API key value from `AdminOptions:ApiKey`, which is bound from the `Admin:ApiKey` configuration section (overridable via the `Admin__ApiKey` environment variable).

#### Scenario: Empty or missing key disables admin routes on startup
- **WHEN** `Admin:ApiKey` is null or empty at startup
- **THEN** the application fails to start with a descriptive error indicating the admin key must be configured
