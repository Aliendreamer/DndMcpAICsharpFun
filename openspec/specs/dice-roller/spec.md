# dice-roller Specification

## Purpose
TBD - created by archiving change dice-roller. Update Purpose after archive.
## Requirements
### Requirement: Parse standard dice notation

The system SHALL parse dice expressions in the form `NdX±K`: an optional count `N` (default 1), a
die size `X` restricted to `{4, 6, 8, 10, 12, 20, 100}`, and an optional integer modifier `+K` or
`-K`. It SHALL also accept an advantage/disadvantage flag valid only for a single d20. Parsing SHALL
return a clear validation error (not throw an unhandled exception) for malformed input: an
unsupported die size, unparseable text, an advantage/disadvantage flag on anything other than a
single d20, or a count above a sane maximum.

#### Scenario: Valid expression parses

- **WHEN** `"2d6+3"` is parsed
- **THEN** it SHALL yield count 2, die 6, modifier +3, no advantage/disadvantage

#### Scenario: Bare die defaults to count 1

- **WHEN** `"d20"` is parsed
- **THEN** it SHALL yield count 1, die 20, modifier 0

#### Scenario: Unsupported die is rejected

- **WHEN** `"1d7"` is parsed
- **THEN** parsing SHALL fail with a clear error and produce no expression

#### Scenario: Advantage only on a single d20

- **WHEN** an advantage/disadvantage flag is applied to a non-d20 die (or to `2d20`)
- **THEN** parsing SHALL fail with a clear error

#### Scenario: Absurd count is rejected

- **WHEN** a count above the configured maximum (e.g. `999d100`) is parsed
- **THEN** parsing SHALL fail with a clear error rather than roll it

### Requirement: Roll an expression deterministically over an injected RNG

The system SHALL roll a parsed expression using an injected `IRandomSource` (the sole source of
nondeterminism), producing each die value in `[1, X]`, applying the modifier to the total, and
returning the individual die values, the modifier, and the total. With a seeded/scripted
`IRandomSource`, the result SHALL be exactly reproducible.

#### Scenario: Rolls are within range

- **WHEN** `3d6` is rolled
- **THEN** each of the three die values SHALL be between 1 and 6 inclusive

#### Scenario: Modifier is applied to the total

- **WHEN** `2d6+3` is rolled and the two dice come up 4 and 5 (scripted RNG)
- **THEN** the total SHALL be 12 and the die values SHALL be reported as 4 and 5

#### Scenario: Seeded RNG is reproducible

- **WHEN** the same expression is rolled twice with the same seeded `IRandomSource` state
- **THEN** both rolls SHALL produce identical results

### Requirement: Advantage and disadvantage keep one of two d20

The system SHALL, for an advantage roll, roll two d20 and keep the higher; for a disadvantage roll,
roll two d20 and keep the lower. Both rolled values SHALL be reported alongside the kept value.

#### Scenario: Advantage keeps the higher

- **WHEN** a d20 advantage roll produces 18 and 7 (scripted RNG)
- **THEN** the kept value SHALL be 18 and both 18 and 7 SHALL be reported

#### Scenario: Disadvantage keeps the lower

- **WHEN** a d20 disadvantage roll produces 18 and 7 (scripted RNG)
- **THEN** the kept value SHALL be 7 and both 18 and 7 SHALL be reported

### Requirement: Roll result carries a human-readable breakdown

Every roll result SHALL include a human-readable breakdown string showing the expression, the
individual (and, for adv/dis, kept) dice, the modifier, and the total.

#### Scenario: Breakdown shows dice, modifier, and total

- **WHEN** `2d6+3` rolls 4 and 5
- **THEN** the breakdown SHALL read like `"2d6+3 → [4,5]+3 = 12"`

#### Scenario: Advantage breakdown shows both dice and the kept value

- **WHEN** a d20 advantage roll produces 18 and 7
- **THEN** the breakdown SHALL read like `"d20 (adv) → [18,7] → 18"`

### Requirement: Dice roller component on the campaign page with ephemeral recent rolls

The system SHALL provide a reusable Blazor `DiceRoller` component, embedded on the campaign detail
page, that lets the user compose an expression (quick-die buttons for d4…d100, a count, a `+/-`
modifier, and an advantage/disadvantage toggle enabled for d20, or a free-text expression), roll it
via the in-process core, and see the result with its breakdown. The component SHALL keep a
session-local list of the recent rolls (most recent first) in component state only; it SHALL NOT
persist rolls to any store in this slice.

#### Scenario: Rolling shows the result and adds to recent rolls

- **WHEN** the user composes an expression and clicks Roll
- **THEN** the component SHALL display the result and breakdown and prepend it to the session recent-rolls list

#### Scenario: Invalid input shows an error, not a crash

- **WHEN** the user submits an unparseable/invalid expression
- **THEN** the component SHALL show the validation error and SHALL NOT roll or throw

#### Scenario: Rolls are not persisted in this slice

- **WHEN** the user rolls dice
- **THEN** no roll SHALL be written to the database or any persistent store (recent rolls are in-memory only)

