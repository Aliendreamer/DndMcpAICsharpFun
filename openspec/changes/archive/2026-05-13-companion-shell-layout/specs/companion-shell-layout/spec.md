## ADDED Requirements

### Requirement: Authenticated pages render inside a left-sidebar shell
The system SHALL provide `AuthLayout.razor` as a separate layout for auth pages and rewrite `MainLayout.razor` as a left-sidebar shell. The sidebar SHALL display the app name, navigation links (Chat, Campaigns, Heroes), the current user's username, and a logout button. Auth pages (`Login.razor`, `Register.razor`) SHALL declare `@layout AuthLayout` to use the centered card layout instead.

#### Scenario: Authenticated user sees sidebar with nav links
- **WHEN** an authenticated user navigates to any non-auth page
- **THEN** the left sidebar is visible with links to Chat (`/`), Campaigns (`/campaigns`), and Heroes (`/heroes`), plus the username and logout button

#### Scenario: Unauthenticated user on login page sees no sidebar
- **WHEN** a user navigates to `/login` or `/register`
- **THEN** no sidebar is rendered — only the centered auth card is shown

#### Scenario: Active nav link is visually highlighted
- **WHEN** the user is on the Chat page
- **THEN** the Chat nav link in the sidebar is visually distinguished from the other links (e.g., different background or text colour)

### Requirement: Logout button in sidebar signs the user out
The system SHALL provide a logout button in the sidebar footer that signs the user out and redirects to `/login`.

#### Scenario: User clicks logout
- **WHEN** an authenticated user clicks the logout button in the sidebar
- **THEN** the user is signed out and redirected to `/login`

### Requirement: Auth pages have styled form CSS
The system SHALL include CSS rules for `.auth-container`, `.form-group`, and `.error` in `app.css` so that login and register forms are visually styled (centred card, labelled inputs, red error text).

#### Scenario: Login form renders with styles
- **WHEN** a user navigates to `/login`
- **THEN** the form is displayed as a centred card with styled inputs and a submit button

### Requirement: Top-level heroes page lists all user heroes
The system SHALL provide a Blazor page at `/heroes` (authorised) that displays all heroes across all of the authenticated user's campaigns. Each hero entry SHALL show the hero name, campaign name, and current level, and SHALL link to the hero's detail page at `/campaigns/{campaignId}/heroes/{heroId}`.

#### Scenario: Heroes page shows heroes from multiple campaigns
- **WHEN** an authenticated user with heroes in two campaigns navigates to `/heroes`
- **THEN** heroes from both campaigns are listed, each with the campaign name and a link to the hero detail page

#### Scenario: Heroes page shows empty state
- **WHEN** an authenticated user with no heroes navigates to `/heroes`
- **THEN** an empty-state message is displayed

### Requirement: HeroRepository exposes a cross-campaign heroes query
The system SHALL add `GetAllByUserAsync(long userId)` to `HeroRepository` that returns all heroes belonging to campaigns owned by the given user in a single SQL query (joining `Heroes` to `Campaigns` on `userId`).

#### Scenario: Query returns heroes across all user campaigns
- **WHEN** `GetAllByUserAsync(userId)` is called for a user with heroes in multiple campaigns
- **THEN** all heroes are returned without issuing per-campaign queries
