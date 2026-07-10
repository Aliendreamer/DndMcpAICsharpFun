## ADDED Requirements

### Requirement: Token layer drives all styling

The stylesheet SHALL define the design system as CSS custom properties on `:root` — a dark palette, a spacing scale, radii, shadows, and a type scale — and all component styling SHALL derive from those tokens rather than hardcoded literals. The palette SHALL be: base `#0E1018`, surface `#171A27`, inset `#20243A`, border `#2A2F48`, arcane-violet `#8B7CF6` (links / focus / secondary interactive), ember-gold `#E8B65A` (primary actions, current-turn illumination, key numbers), damage `#E5484D`, heal `#3DD68C`, muted `#8A90A6`, text `#E7E9F2`.

#### Scenario: Colors come from tokens, not literals

- **WHEN** the stylesheet is inspected
- **THEN** the palette, spacing, radii, and type scale SHALL be defined once as `:root` custom properties, and component rules SHALL reference them via `var(--…)` rather than repeating raw hex/px values

### Requirement: Self-hosted typography in three roles

The system SHALL load three OFL-licensed typefaces self-hosted as woff2 under `wwwroot/fonts/` via `@font-face` (no external CDN, so the UI renders offline): a blackletter **display** face used only for the wordmark, page `h1`, and the combat name; a humanist **body** face for UI text; and a **mono** face for data readouts (dice breakdown, HP, initiative, CR, XP). A clear type scale with intentional weights SHALL be set.

#### Scenario: Fonts load from the app, not a CDN

- **WHEN** the app is served with no internet access
- **THEN** the display, body, and mono faces SHALL still render from `wwwroot/fonts/` (no external font request)

#### Scenario: The display face is used with restraint

- **WHEN** any surface is inspected
- **THEN** the blackletter display face SHALL appear only on the wordmark, page headings, and the combat name — never on control labels, body copy, or data

### Requirement: Shared UI primitives

The system SHALL provide consistent, token-derived primitives reused across surfaces: buttons (a primary ember style, a secondary/ghost style, and a danger style), form controls (text inputs, selects, number steppers), cards/panels, chips (conditions and tags), and badges. Interactive elements SHALL have a visible hover and a visible keyboard-focus state.

#### Scenario: The same control looks the same everywhere

- **WHEN** a primary action button appears on two different surfaces
- **THEN** both SHALL render with the same token-derived primary (ember) style

#### Scenario: Keyboard focus is visible

- **WHEN** the user tabs to an interactive control
- **THEN** a visible focus indicator (arcane) SHALL be shown

### Requirement: Every surface is styled — no raw unstyled components

Every companion surface SHALL be styled against the system with no component left rendering as raw default HTML: the sidebar/layout, auth (login/register), the campaigns list, campaign detail, the table page and its four components (dice roller, encounter builder, initiative tracker, campaign log), the heroes list and hero detail, and chat. Class names referenced by markup SHALL have corresponding rules in the stylesheet.

#### Scenario: The table page renders styled, not raw

- **WHEN** the campaign table page (`/campaigns/{id}/table`) is viewed
- **THEN** the dice roller, encounter builder, initiative tracker, and campaign log SHALL all render with the design system's styling (no default-browser inputs/buttons), and every class the markup references SHALL resolve to a rule

### Requirement: The initiative rail is the signature element

The initiative tracker SHALL be the visual centerpiece of the table page: an ordered rail of combatants where the **current combatant is illuminated** with an ember edge and glow, the round counter is prominent, HP is shown as a number plus a thin bar, and conditions are collapsed to only the *active* chips plus an add ("+") affordance rather than an inline row of all fifteen condition buttons.

#### Scenario: The current combatant stands out

- **WHEN** a combat has an active turn
- **THEN** the current combatant's row SHALL be visually illuminated (ember edge/glow) so it reads at a glance as whose turn it is

#### Scenario: Conditions are collapsed until added

- **WHEN** a combatant has no conditions
- **THEN** the row SHALL show an add ("+") affordance and SHALL NOT render all fifteen condition buttons inline; only conditions that are set SHALL appear as chips

### Requirement: Responsive and accessible quality floor

The UI SHALL be responsive down to a mobile viewport (the sidebar collapses rather than overflowing), SHALL honor `prefers-reduced-motion` (ambient/hover animation disabled when the user requests reduced motion), and SHALL meet WCAG AA contrast for body text against its background.

#### Scenario: Mobile layout does not overflow

- **WHEN** the app is viewed at a narrow (mobile) width
- **THEN** the layout SHALL adapt (sidebar collapses) and the page body SHALL NOT scroll horizontally

#### Scenario: Reduced motion is respected

- **WHEN** the user's system requests reduced motion
- **THEN** ambient and hover animations SHALL be disabled while the layout and state styling remain intact
