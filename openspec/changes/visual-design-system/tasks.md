## 1. Tokens, fonts, base

- [ ] 1.1 Add self-hosted woff2 faces under `wwwroot/fonts/` (display=Grenze Gotisch, body=Alegreya Sans, data=JetBrains Mono; only the weights used) + `@font-face` rules
- [ ] 1.2 Define `:root` tokens in `app.css`: the confirmed palette (base `#0E1018`, surface `#171A27`, inset `#20243A`, border `#2A2F48`, arcane `#8B7CF6`, ember `#E8B65A`, hp `#E5484D`, heal `#3DD68C`, muted `#8A90A6`, text `#E7E9F2`), a spacing scale, radii, shadows, and a type scale (display/body/mono families + sizes/weights)
- [ ] 1.3 Base layer: reset refinement, `body` background/typography from tokens, a very subtle candlelight vignette on the base, `prefers-reduced-motion` disabling ambient/hover motion

## 2. Shared primitives

- [ ] 2.1 Buttons: `.btn` + `.btn--primary` (ember), `.btn--ghost`/secondary (arcane), `.btn--danger` (hp); hover + visible focus-ring (arcane)
- [ ] 2.2 Form controls: text inputs, selects, the dice number-stepper, checkboxes — token-derived, consistent sizing, focus states
- [ ] 2.3 Containers + chips + badges: `.card`/panel, `.chip` (condition/tag), `.badge` (hidden/kind) — single-class selectors to avoid specificity fights

## 3. Chrome surfaces

- [ ] 3.1 Sidebar / `MainLayout`: arcane rail, Grenze wordmark, gold active-route indicator; responsive collapse at mobile width
- [ ] 3.2 Auth (`Login`, `Register`): centered card, styled form fields + primary CTA, error styling
- [ ] 3.3 Campaigns list + `CampaignDetail`: campaign cards, hero-roster cards, the "▶ Run session" CTA, notes; keep existing empty-state copy

## 4. Table page (the signature) — most new CSS

- [ ] 4.1 `CampaignTable` page shell + section rhythm hosting the four components
- [ ] 4.2 `DiceRollerPanel`: quick-die buttons, count stepper, modifier, adv/dis toggle, label picker; the result as a hero "cast" (big ember total + mono breakdown)
- [ ] 4.3 `EncounterPanel`: controls row, built-encounter result, save row
- [ ] 4.4 `InitiativeTracker` — the initiative rail: ordered rows, **current-turn illumination** (ember edge + glow), prominent round counter, HP number + thin bar, editable init/MaxHp fields styled; **collapse conditions** to active-chips-plus-"+" (markup change in `InitiativeTracker.razor`, `ToggleConditionAsync` unchanged); start form + end-combat review panel + history list styled
- [ ] 4.5 `CampaignLog`: timeline entries (roll / encounter / combat), hidden badge + reveal/delete controls

## 5. Remaining surfaces

- [ ] 5.1 Heroes list + `HeroDetail` character sheet
- [ ] 5.2 Chat: refine bubbles/header/input against the tokens (already partly styled)

## 6. Verify

- [ ] 6.1 `dotnet build` 0/0; full `dotnet test` suite stays green (behavior unchanged); confirm every markup-referenced class now resolves to a rule (no unstyled component); confirm no `.http`/`.insomnia` change
- [ ] 6.2 Rebuild the dev container from the branch; Playwright-screenshot every surface at desktop AND mobile width (incl. the table page in the illuminated-turn state); check WCAG-AA body contrast, visible focus, no horizontal overflow, reduced-motion honored
- [ ] 6.3 User reviews the screenshots at each surface-review gate; iterate on any redirect; final whole-branch review
