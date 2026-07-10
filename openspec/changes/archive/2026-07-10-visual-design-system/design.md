## Context

`wwwroot/app.css` (430 lines) styles the Blazor Server UI with no token layer — every value is a one-off literal, so surfaces drift. Several recently-shipped components reference classes that were never added to the stylesheet (`.initiative-tracker`, `.combatant`, `.c-hp`, `.cond`, `.encounter-panel`, `.campaign-log`, `.hero-card`, `.campaign-table`, `.run-session`), so they render unstyled. The app is served from one host; `wwwroot` is published as static web assets. The design direction ("the arcane console" — a dark candle-lit DM table surface) and the **near-neutral slate** palette were chosen with the user against a live side-by-side mockup.

## Goals / Non-Goals

**Goals:**

- One token-driven design system every surface renders through.
- Kill the unstyled table-play components; make the initiative tracker the signature.
- Offline-capable self-hosted fonts; a responsive, accessible quality floor.

**Non-Goals:**

- No behavior, routing, domain, persistence, DI, or MCP change; no new endpoint/tool.
- No light theme (dark-only is deliberate for a dim-room table tool).
- No component-framework migration; Blazor structure and markup semantics stay — edits are limited to CSS classes, minimal wrappers, and the conditions-collapse affordance.

## Decisions

**Near-neutral slate palette (Option 3), gold + violet accents.** Chosen from a live mockup: base `#0E1018`, surface `#171A27`, inset `#20243A`, border `#2A2F48`; ember-gold `#E8B65A` is where boldness is spent (primary actions, current-turn illumination, key numbers); arcane-violet `#8B7CF6` stays quiet (links, focus, secondary). Semantic damage `#E5484D` / heal `#3DD68C` are separate from the accents. *Alternative considered:* a warmer plum-obsidian ground — rejected by the user after seeing both; the accents + illuminated turn row carry enough warmth on the slate ground.

**Self-hosted fonts, three roles.** Display = a blackletter (Grenze Gotisch) strictly for wordmark / `h1` / combat name — the one deliberate risk, kept off labels and data so it reads "spell tome," not cheesy; fall back to Cinzel if it reads too heavy in review. Body = Alegreya Sans (warm humanist). Data = JetBrains Mono (dice breakdown, HP, initiative, CR, XP — a "readout" feel). All OFL, self-hosted as woff2 under `wwwroot/fonts/` with `@font-face` — no CDN, so the local tool renders offline. *Alternative:* Google Fonts CDN — rejected (network dependency for a local-first app).

**Token layer first, then primitives, then surfaces.** `:root` custom properties (color, spacing scale, radii, shadows, type scale) → shared primitives (buttons/inputs/cards/chips/badges) → per-surface styling. This ordering means each surface is assembled from already-consistent parts. *Alternative:* style surface-by-surface with local values — rejected; that is how the current drift happened.

**Signature = the illuminated initiative rail; collapse the conditions.** "Whose turn" is the table's core question, so the current combatant gets an ember edge + glow and the round counter is prominent. Today every combatant row renders all 15 condition buttons inline (visually noisy and the biggest eyesore in the smoke); collapse to active-chips-plus-"+" — a small markup change in `InitiativeTracker.razor` (a popover/menu of the 15, or a toggle that reveals them), with the existing `ToggleConditionAsync` handler unchanged.

**One stylesheet, sectioned.** Keep a single `app.css` (already linked in `App.razor`) organized into commented sections: tokens → base/reset → fonts → primitives → layout → each surface. If it grows unwieldy it can split into `@import`ed partials, but a single sectioned file avoids extra static-asset wiring. Guard against specificity foot-guns: primitives use single-class selectors; surfaces scope with a page/container class; no element-selector vs class-selector fights over padding/margins.

## Risks / Trade-offs

- **[Blackletter reads cheesy or hurts legibility]** → Restrict it to wordmark/`h1`/combat-name only; review in screenshots; swap to Cinzel if needed. Body and data never use it.
- **[Self-hosted font weight bloats first paint]** → Ship only the weights actually used (≈1–2 per family), `font-display: swap`, woff2 only.
- **[CSS specificity cancels spacing between sections]** → Layout with flex/grid `gap`, not sibling margins; keep primitive selectors single-class; verify each surface visually.
- **[Markup-class edits accidentally change behavior]** → Edits are limited to class names, wrappers, and the conditions-collapse UI; no handler/logic changes; the full test suite must stay green (nothing should depend on styling) and each surface is screenshot-verified.
- **[Contrast regressions on the slate ground]** → Check body text (`--text` on `--surface`/`--ink`) meets WCAG AA; muted text used only for secondary content.

## Verification

No unit tests (pure CSS + markup-class change). Verify by: `dotnet build` 0/0; the full `dotnet test` suite stays green (behavior unchanged); the dev container rebuilt from the branch; and **Playwright screenshots of every surface** (login, campaigns, campaign detail, the table page + its four components in the illuminated state, heroes, chat) reviewed against the design — at a desktop width and a mobile width. The user reviews the screenshots at the surface-review gates.
