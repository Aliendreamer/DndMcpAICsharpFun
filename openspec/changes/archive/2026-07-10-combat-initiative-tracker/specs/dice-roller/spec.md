## MODIFIED Requirements

### Requirement: Dice roller component on the campaign page with ephemeral recent rolls

The system SHALL provide a reusable Blazor `DiceRoller` component, embedded on the campaign **play
(table) page** (`/campaigns/{id}/table`), that lets the user compose an expression (quick-die
buttons for d4…d100, a count, a `+/-` modifier, and an advantage/disadvantage toggle enabled for
d20, or a free-text expression), roll it via the in-process core, and see the result with its
breakdown. The component SHALL keep a session-local list of the recent rolls (most recent first) in
component state only; it SHALL NOT persist rolls to any store in this slice. (Persistent auto-logging
of rolls remains the concern of the `campaign-log-history` capability.)

#### Scenario: Rolling shows the result and adds to recent rolls

- **WHEN** the user composes an expression and clicks Roll
- **THEN** the component SHALL display the result and breakdown and prepend it to the session recent-rolls list

#### Scenario: Invalid input shows an error, not a crash

- **WHEN** the user submits an unparseable/invalid expression
- **THEN** the component SHALL show the validation error and SHALL NOT roll or throw

#### Scenario: Rolls are not persisted in this slice

- **WHEN** the user rolls dice
- **THEN** no roll SHALL be written to the database or any persistent store by the component itself (recent rolls are in-memory only)
