## ADDED Requirements

### Requirement: Users can register with username and password
The companion SHALL provide a `/register` page where a new user enters a username and password. The password SHALL be hashed with PBKDF2/SHA-256 before storage. Duplicate usernames SHALL be rejected with a validation message.

#### Scenario: Successful registration
- **WHEN** a user submits a unique username and a password of at least 8 characters
- **THEN** the account is created, the user is signed in, and they are redirected to the chat page

#### Scenario: Duplicate username
- **WHEN** a user submits a username that already exists
- **THEN** a validation message is shown and no account is created

#### Scenario: Password too short
- **WHEN** a user submits a password shorter than 8 characters
- **THEN** a validation message is shown and no account is created

### Requirement: Users can log in and out
The companion SHALL provide a `/login` page accepting username and password. On success, an authentication cookie is issued. A logout action SHALL revoke the cookie and redirect to `/login`.

#### Scenario: Successful login
- **WHEN** a user submits correct credentials on `/login`
- **THEN** an auth cookie is set and the user is redirected to the chat page

#### Scenario: Wrong credentials
- **WHEN** a user submits an incorrect username or password
- **THEN** a generic error message is shown ("Invalid username or password") with no account enumeration

#### Scenario: Logout
- **WHEN** a logged-in user clicks Logout
- **THEN** the auth cookie is revoked and the user is redirected to `/login`

### Requirement: User accounts are persisted in companion SQLite
The companion SHALL store accounts in a `Users` table in `data/companion.db` with columns `Id` (integer PK), `Username` (text unique), and `PasswordHash` (text).

#### Scenario: Account survives container restart
- **WHEN** the companion container is restarted
- **THEN** previously registered users can still log in
