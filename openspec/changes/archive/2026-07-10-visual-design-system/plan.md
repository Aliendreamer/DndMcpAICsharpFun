# Visual Design System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan. UI tasks are verified by **build + screenshots**, not unit tests — after each surface, the controller rebuilds the dev container and Playwright-screenshots it for review. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Give the Blazor UI one intentional "arcane console" identity — a token-driven design system + self-hosted fonts + every surface restyled, with the initiative tracker as the signature.

**Architecture:** All styling lives in `wwwroot/app.css`, rewritten from ad-hoc hex into a sectioned, token-driven system (tokens → base → fonts → primitives → layout → per-surface). Self-hosted woff2 fonts under `wwwroot/fonts/`. Markup edits are limited to CSS classes, thin wrappers, and one behavior-neutral affordance (conditions collapse in `InitiativeTracker.razor`).

**Tech Stack:** .NET 10 Blazor Server, plain CSS (custom properties), self-hosted woff2 (from `@fontsource` npm packages), Playwright for visual verification.

## Global Constraints

- **Palette (confirmed — Option 3 slate), exact hex** — base `#0E1018`, surface `#171A27`, inset/`surface-2` `#20243A`, border `#2A2F48`, arcane-violet `#8B7CF6`, ember-gold `#E8B65A`, damage `#E5484D`, heal `#3DD68C`, muted `#8A90A6`, text `#E7E9F2`. Gold = primary actions / current-turn / key numbers; violet = links/focus/secondary only.
- **Fonts, self-hosted woff2, no CDN** — display **Grenze Gotisch** (wordmark / page `h1` / combat name ONLY), body **Alegreya Sans**, data **JetBrains Mono**. `@font-face` with `font-display: swap`.
- **Presentational only** — NO logic/handler/routing/domain/DI/MCP change; NO new endpoint/tool; do NOT touch `DndMcpAICsharpFun.http` or `dnd-mcp-api.insomnia.json`. The full `dotnet test` suite MUST stay green (nothing depends on styling).
- **Dark-only** (no light theme). **warnings-as-errors** on build (0/0). `dotnet` runs need `dangerouslyDisableSandbox: true`.
- **Quality floor** — responsive to mobile (sidebar collapses, no horizontal body scroll), visible keyboard focus (arcane ring), `prefers-reduced-motion` disables ambient/hover motion, WCAG-AA body text contrast.
- **CSS discipline** — primitives are single-class selectors; surfaces scope under a page/container class; layout uses flex/grid `gap` (not sibling margins); no element-vs-class selector fights over padding/margins.
- **Serena for `.razor` edits**; built-in Read/Edit forbidden on code files. `app.css` and font files are assets — editing `app.css` directly is fine.

## File Structure

- Create `wwwroot/fonts/*.woff2` — self-hosted faces (only the weights used).
- Rewrite `wwwroot/app.css` — sectioned: `/* tokens */ /* fonts */ /* base */ /* primitives */ /* layout */ /* surface: auth */ …`.
- Edit `CompanionUI/Components/App.razor` — `<link rel="preload">` the display + body woff2 (optional; `@font-face` already covers it).
- Edit (classes/wrappers only): `CompanionUI/Layout/MainLayout.razor`, `CompanionUI/Pages/**` (Login, Register, Campaigns, CampaignDetail, CampaignTable, Heroes, HeroDetail, Chat), `CompanionUI/Components/**` (DiceRollerPanel, EncounterPanel, InitiativeTracker, CampaignLog).
- One behavior-neutral markup change: conditions collapse in `InitiativeTracker.razor`.

---

### Task 1: Fonts + token layer + base

**Files:** Create `wwwroot/fonts/*.woff2`; rewrite the top of `wwwroot/app.css` (tokens/fonts/base sections).

- [ ] **Step 1: Fetch the self-hosted woff2 files**

Run from repo root (registry.npmjs.org is reachable):

```bash
mkdir -p wwwroot/fonts && cd "$(mktemp -d)" && \
for p in grenze-gotisch alegreya-sans jetbrains-mono; do npm pack @fontsource/$p@latest >/dev/null 2>&1; done && \
for t in *.tgz; do tar xzf "$t"; done && \
cp package*/files/grenze-gotisch-latin-700-normal.woff2 "$OLDPWD/wwwroot/fonts/grenze-gotisch-700.woff2" 2>/dev/null || \
  find . -name 'grenze-gotisch-latin-700-normal.woff2' -exec cp {} "$OLDPWD/wwwroot/fonts/grenze-gotisch-700.woff2" \;
find . -name 'alegreya-sans-latin-400-normal.woff2' -exec cp {} "$OLDPWD/wwwroot/fonts/alegreya-sans-400.woff2" \;
find . -name 'alegreya-sans-latin-500-normal.woff2' -exec cp {} "$OLDPWD/wwwroot/fonts/alegreya-sans-500.woff2" \;
find . -name 'alegreya-sans-latin-700-normal.woff2' -exec cp {} "$OLDPWD/wwwroot/fonts/alegreya-sans-700.woff2" \;
find . -name 'jetbrains-mono-latin-400-normal.woff2' -exec cp {} "$OLDPWD/wwwroot/fonts/jetbrains-mono-400.woff2" \;
find . -name 'jetbrains-mono-latin-600-normal.woff2' -exec cp {} "$OLDPWD/wwwroot/fonts/jetbrains-mono-600.woff2" \;
cd "$OLDPWD" && ls -la wwwroot/fonts/
```

Expected: `wwwroot/fonts/` contains grenze-gotisch-700, alegreya-sans-400/500/700, jetbrains-mono-400/600 woff2 files. (If Grenze Gotisch has no 700, use 400 and set `font-weight` accordingly.)

- [ ] **Step 2: Write the tokens + fonts + base sections at the top of `app.css`**

Replace the current first block (the `*` reset + `body`) with:

```css
/* ============ fonts ============ */
@font-face { font-family: "Grenze Gotisch"; src: url("fonts/grenze-gotisch-700.woff2") format("woff2"); font-weight: 700; font-display: swap; }
@font-face { font-family: "Alegreya Sans"; src: url("fonts/alegreya-sans-400.woff2") format("woff2"); font-weight: 400; font-display: swap; }
@font-face { font-family: "Alegreya Sans"; src: url("fonts/alegreya-sans-500.woff2") format("woff2"); font-weight: 500; font-display: swap; }
@font-face { font-family: "Alegreya Sans"; src: url("fonts/alegreya-sans-700.woff2") format("woff2"); font-weight: 700; font-display: swap; }
@font-face { font-family: "JetBrains Mono"; src: url("fonts/jetbrains-mono-400.woff2") format("woff2"); font-weight: 400; font-display: swap; }
@font-face { font-family: "JetBrains Mono"; src: url("fonts/jetbrains-mono-600.woff2") format("woff2"); font-weight: 600; font-display: swap; }

/* ============ tokens ============ */
:root {
  --ink: #0E1018; --surface: #171A27; --surface-2: #20243A; --border: #2A2F48;
  --arcane: #8B7CF6; --ember: #E8B65A; --hp: #E5484D; --heal: #3DD68C;
  --muted: #8A90A6; --text: #E7E9F2;
  --font-display: "Grenze Gotisch", Georgia, serif;
  --font-body: "Alegreya Sans", system-ui, sans-serif;
  --font-data: "JetBrains Mono", ui-monospace, monospace;
  --s1:4px; --s2:8px; --s3:12px; --s4:16px; --s5:24px; --s6:32px; --s7:48px;
  --r-sm:7px; --r-md:10px; --r-lg:14px; --r-pill:999px;
  --shadow: 0 18px 50px -28px rgba(0,0,0,.85);
  --ring: 0 0 0 2px var(--ink), 0 0 0 4px var(--arcane);
  --fs-xs:12px; --fs-sm:13.5px; --fs-md:15px; --fs-lg:19px; --fs-xl:26px; --fs-2xl:38px;
}

/* ============ base ============ */
*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
body {
  font-family: var(--font-body); color: var(--text);
  background:
    radial-gradient(120% 80% at 50% -10%, rgba(232,182,90,.05), transparent 55%),
    var(--ink);
  min-height: 100vh; display: flex; flex-direction: column; line-height: 1.5;
  -webkit-font-smoothing: antialiased;
}
h1, h2, h3 { text-wrap: balance; }
a { color: var(--arcane); text-decoration: none; }
a:hover { text-decoration: underline; }
:focus-visible { outline: none; box-shadow: var(--ring); border-radius: var(--r-sm); }
.muted { color: var(--muted); }
.error { color: var(--hp); font-size: var(--fs-sm); }
@media (prefers-reduced-motion: reduce) { *, *::before, *::after { animation: none !important; transition: none !important; } }
```

- [ ] **Step 3: Build + first screenshot**

Run `dotnet build` (dangerouslyDisableSandbox) → 0/0. (The controller rebuilds the dev container and screenshots after primitives land in Task 2 — this task alone won't look finished.)

- [ ] **Step 4: Commit**

```bash
git add wwwroot/fonts wwwroot/app.css
git commit -m "feat(ui): design tokens + self-hosted arcane-console fonts + base layer"
```

---

### Task 2: Shared primitives

**Files:** append a `/* primitives */` section to `wwwroot/app.css`.

- [ ] **Step 1: Write the primitives**

Add token-derived rules for: buttons, form controls, cards, chips, badges. Complete CSS:

```css
/* ============ primitives ============ */
button { font-family: var(--font-body); }
.btn, button.btn { display:inline-flex; align-items:center; gap:var(--s2); border:1px solid transparent;
  border-radius:var(--r-md); padding:8px 14px; font-size:var(--fs-sm); font-weight:600; cursor:pointer;
  background:var(--surface-2); color:var(--text); transition:filter .12s ease, background .12s ease; }
.btn:hover { filter:brightness(1.12); }
.btn--primary { background:var(--ember); color:#221803; box-shadow:0 0 22px -8px rgba(232,182,90,.55); }
.btn--ghost { background:transparent; color:var(--arcane); border-color:var(--border); }
.btn--danger { background:transparent; color:var(--hp); border-color:var(--border); }

input[type=text], input[type=number], input[type=password], select, textarea {
  font-family:var(--font-body); font-size:var(--fs-sm); color:var(--text);
  background:var(--surface-2); border:1px solid var(--border); border-radius:var(--r-sm);
  padding:8px 10px; }
input::placeholder, textarea::placeholder { color:var(--muted); }

.card { background:var(--surface); border:1px solid var(--border); border-radius:var(--r-lg);
  padding:var(--s4); box-shadow:var(--shadow); }

.chip { display:inline-flex; align-items:center; gap:4px; font-size:var(--fs-xs); padding:2px 9px;
  border-radius:var(--r-pill); background:rgba(139,124,246,.14); color:#c3b8ff; border:1px solid rgba(139,124,246,.3); }
.badge { font-size:var(--fs-xs); padding:2px 8px; border-radius:var(--r-pill);
  background:var(--surface-2); color:var(--muted); border:1px solid var(--border); }

.data { font-family:var(--font-data); font-variant-numeric:tabular-nums; }
```

- [ ] **Step 2: Build** → `dotnet build` 0/0.

- [ ] **Step 3: Commit**

```bash
git add wwwroot/app.css
git commit -m "feat(ui): shared primitives (buttons, inputs, cards, chips, badges) from tokens"
```

---

### Task 3: Chrome surfaces (nav, auth, campaigns, campaign detail)

**Files:** `app.css` (`/* layout */`, `/* surface: auth/campaigns */`); class-only edits in `MainLayout.razor`, `Login.razor`, `Register.razor`, `Campaigns.razor`, `CampaignDetail.razor` where a needed class is missing.

Read each `.razor` first (Serena) to inventory the classes it uses; add a rule for every one. Spec per surface:

- [ ] **Step 1: Sidebar / MainLayout** — `.app-layout` is a 2-col grid (fixed sidebar + fluid main); `.sidebar` = `--surface` with a right hairline; `.sidebar-brand` in `--font-display` + `--ember`; `.sidebar-nav a` rows with hover; `.active` gets a gold left-edge indicator (`border-left:3px solid var(--ember)` or `::before`); footer pinned bottom. At `max-width:820px` the sidebar collapses to a top bar (flex row) so the main content is full-width and the body never scrolls sideways.
- [ ] **Step 2: Auth** — center `.auth-wrapper`; `.auth-card` = `.card` max-width ~380px; `.form-group` label + input stacked with `gap`; primary submit uses `.btn--primary`; `.error` styled. (Keep existing `btn-primary`/`auth-container` class names if the markup uses them — add rules matching those exact names.)
- [ ] **Step 3: Campaigns + CampaignDetail** — campaign cards on the list (`.card` grid, hover lift, name in body face, meta muted); CampaignDetail: `.hero-roster` grid of `.hero-card` (name, class·level, HP/AC as `.data`), the `.run-session` link as a prominent `.btn--primary`, notes as cards. Preserve the empty-state copy.

- [ ] **Step 4: Build + screenshot review** — `dotnet build` 0/0; controller rebuilds the dev container and screenshots login, campaigns, campaign detail (desktop + mobile). Iterate on redirects.

- [ ] **Step 5: Commit** — `git commit -m "feat(ui): style chrome — sidebar, auth, campaigns, campaign detail"`

---

### Task 4: The table page + its four components (the signature)

**Files:** `app.css` (`/* surface: table */`); class-only edits in `CampaignTable.razor`, `DiceRollerPanel.razor`, `EncounterPanel.razor`, `CampaignLog.razor`; the conditions-collapse markup change in `InitiativeTracker.razor`.

- [ ] **Step 1: Table page shell** — `.campaign-table` stacks the four panels with `--s5` gap; a back link + `h1` (combat context) in display face.
- [ ] **Step 2: DiceRollerPanel** — `.dice-quick-buttons` as a row of die chips; `.dice-count-stepper`/`.dice-modifier` styled; `.dice-adv-toggle` as a segmented control; the latest result (`.dice-result-latest`/`.dice-total`) is the hero: big `--ember` total in `--font-data` with a soft glow, `.dice-breakdown` in mono `--muted`; recent list compact.
- [ ] **Step 3: EncounterPanel** — `.encounter-controls` as a wrap row of labeled selects + Build; the built-encounter result in a card; the save row (label + hidden checkbox + save) aligned.
- [ ] **Step 4: InitiativeTracker — the signature rail + conditions collapse.**
  - Style: `.initiative-tracker` container; `.combat-head` (name in display face, round counter as a mono pill in `--ember`, Advance = `.btn--primary`); each `.combatant` row = `.surface-2` with init (mono, min-width, right-aligned), name, HP (a number + a thin bar: `.hp-bar > span` width = `CurrentHp/MaxHp`, colored `--hp`), and controls.
  - **Current turn illumination:** the current row gets `.current` → ember left-edge (`::before` 3px ember bar with glow) + faint ember gradient background + `box-shadow` glow; init number turns `--ember`.
  - **Conditions collapse (markup change, behavior-neutral):** today the row renders all 15 `Enum.GetValues<Condition>()` as buttons inline. Change `InitiativeTracker.razor` so the row shows only the combatant's *active* conditions as `.chip`s, plus a single "+" button that reveals the full 15-condition picker (a small popover/menu, e.g. a `_conditionMenuFor` combatant-id toggle in `@code`, or a `<details>`). The existing `ToggleConditionAsync(c, cond)` handler is reused unchanged — only the rendering wraps in the collapse. Verify the full test suite still passes (no handler/logic change).
  - Style the end-combat review panel (`.end-combat-review`, before→after HP rows) and the history `<details>`.
- [ ] **Step 5: CampaignLog** — `.log-entries` as a timeline; each `.log-entry` a row (kind marker, `.log-line`, time); hidden entries get a `.badge` + reveal/delete controls; combat/roll/encounter lines legible.
- [ ] **Step 6: Build + screenshot review** — `dotnet build` 0/0; **full `dotnet test` suite green** (proves the conditions-collapse markup change is behavior-neutral); controller rebuilds the dev container and screenshots the table page in the illuminated-turn state (desktop + mobile). Iterate.
- [ ] **Step 7: Commit** — `git commit -m "feat(ui): table page — dice cast, encounter, signature initiative rail, collapsed conditions, log"`

---

### Task 5: Remaining surfaces (heroes, chat)

**Files:** `app.css` (`/* surface: heroes/chat */`); class-only edits in `Heroes.razor`, `HeroDetail.razor`, `Chat.razor` as needed.

- [ ] **Step 1: Heroes + HeroDetail** — heroes list as cards; the HeroDetail character sheet (`.sheet-section`, `.ability-grid`/`.ability-block`/`.ability-score`/`.ability-mod`/`.ability-label`, `.edit-grid`, `.identity-grid`, `.spell-slots`/`.slot-grid`, `.history-*`, `.save-prompt`, `.advisory`) styled as a tidy sheet: ability blocks as bordered tiles with the score big (mono) and modifier below; sections as cards; numbers in `.data`.
- [ ] **Step 2: Chat** — the chat already has working styles; re-derive its colors from tokens (bubbles, header, input row) so it matches the system; keep the layout.
- [ ] **Step 3: Build + screenshot review** — `dotnet build` 0/0; screenshot heroes, hero detail, chat (desktop + mobile). Iterate.
- [ ] **Step 4: Commit** — `git commit -m "feat(ui): style heroes, hero sheet, and chat against the system"`

---

### Task 6: Whole-app verification + review

**Files:** none (verification only).

- [ ] **Step 1** — `dotnet build` 0/0; **full `dotnet test`** suite green (behavior unchanged).
- [ ] **Step 2** — Confirm every class referenced in any `.razor` resolves to a rule (grep the razor class list against `app.css`); no component renders raw. Confirm `git diff --name-only` shows NO change to `DndMcpAICsharpFun.http` / `dnd-mcp-api.insomnia.json` and no `.cs`/domain/DI change beyond the InitiativeTracker conditions-collapse.
- [ ] **Step 3** — Rebuild the dev container from the branch; Playwright-screenshot every surface at desktop (~1280) AND mobile (~390) width; verify: visible keyboard focus, no horizontal body scroll at mobile, `prefers-reduced-motion` honored, WCAG-AA body contrast (`--text` on `--surface`/`--ink`).
- [ ] **Step 4** — User reviews the final screenshot set; apply any redirects.
- [ ] **Step 5** — Final whole-branch review (opus): scope creep (no behavior change), specificity foot-guns, contrast, the display face restricted to wordmark/h1/combat-name, the initiative-rail signature + conditions-collapse.

## Self-Review

- **Spec coverage:** token layer (T1), self-hosted 3-role fonts (T1), primitives (T2), every-surface-styled (T3–5), signature rail + conditions-collapse (T4), quality floor (T6). All six spec requirements map to tasks.
- **Placeholder scan:** foundation (tokens/fonts/primitives) has exact code; surface tasks give per-class visual specs + the screenshot loop (UI can't be fully pre-specified as literal CSS — the intent + states + verification are concrete).
- **Behavior safety:** the only markup logic touched is the conditions-collapse rendering wrapper (handler reused); every table/table-suite run must stay green.
