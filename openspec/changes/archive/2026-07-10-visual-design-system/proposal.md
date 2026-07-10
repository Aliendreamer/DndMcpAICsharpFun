## Why

The companion works but looks unfinished. `wwwroot/app.css` has **no design tokens** — every color, space, and radius is a one-off hex — so nothing is visually consistent. Worse, the recently-shipped table-play components (`InitiativeTracker`, `EncounterPanel`, `CampaignLog`, and the `CampaignTable`/`CampaignDetail` markup) reference CSS class names that **don't exist in `app.css`**, so they render as raw, unstyled HTML — naked inputs, default buttons, a wall of 15 condition buttons. This change gives the app one intentional visual identity and styles every surface against it.

## What Changes

- **Introduce a token layer** — CSS custom properties on `:root` for a dark "arcane console" palette, a spacing scale, radii, shadows, and a type scale. Everything downstream derives from these tokens.
- **Self-host three typefaces** (woff2 under `wwwroot/fonts/`, `@font-face`, no CDN): a blackletter **display** face (wordmark / page H1 / combat name only), a humanist **body** face, and a **mono** face for data (dice breakdowns, HP, initiative, CR/XP).
- **Add shared primitives** — buttons (primary / secondary / danger), inputs, selects, number-steppers, cards, chips, badges — all derived from tokens, replacing today's ad-hoc per-component styles.
- **Restyle every surface** against the system: sidebar/layout, auth, campaigns list, campaign detail, the **table page and its four components**, heroes, and chat. The previously-unstyled table-play components get the most new CSS.
- **Signature element — the initiative rail:** the initiative tracker becomes the visual centerpiece; the current combatant is illuminated (gold edge + glow), and conditions collapse to only the *active* chips plus a "+" affordance instead of the current inline wall of all 15.
- **Quality floor:** responsive to mobile (sidebar collapses), visible keyboard focus, `prefers-reduced-motion` honored, WCAG-AA text contrast.

This is **presentational only** — no behavior, routing, domain, persistence, or MCP changes; no new HTTP route or tool.

## Capabilities

### New Capabilities

- `visual-design-system`: the token-based design system (palette, type, spacing/radius/shadow scales, self-hosted fonts) and the shared UI primitives + per-surface styling that every companion screen renders through, including the signature illuminated initiative rail and the collapsed-conditions affordance.

### Modified Capabilities

<!-- None. Existing UI specs (companion-ui-layout, sidebar-navigation, ui-empty-states) assert structure/behavior (source layout, active-route highlight, empty-state copy), not visual styling — all remain satisfied. This change is purely presentational and adds no requirement-level behavior change to them. -->

## Impact

- **New files**: `wwwroot/fonts/*.woff2` (self-hosted OFL faces), served as static web assets.
- **Rewritten**: `wwwroot/app.css` — from ad-hoc hex styles to a token-driven system (may be split into a few files if it grows; decided in the plan).
- **Light markup edits** (CSS classes + minimal wrappers only, no logic): `CompanionUI/Layout/MainLayout.razor`, `CompanionUI/Pages/**` (auth, campaigns, campaign detail, campaign table, heroes, chat), and `CompanionUI/Components/**` (`DiceRollerPanel`, `EncounterPanel`, `InitiativeTracker` — plus the conditions-collapse affordance, `CampaignLog`).
- **No** `.http` / `.insomnia` change (no endpoint/tool touched). **No** domain/persistence/DI change.
- **Verification**: build 0/0, the running app renders (dev container on `localhost:5101`), and Playwright screenshots of each surface are reviewed. No unit tests (pure CSS/markup-class change).
