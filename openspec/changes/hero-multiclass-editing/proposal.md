## Why

The domain model and resolution layer are fully multiclass-aware (archived `multiclass-character`, 2026-07-05): `CharacterSheet.Classes: List<ClassLevel>` is the source of truth and the resolution engine reasons over per-class levels. But the **Blazor edit UI never caught up.** `HeroDetail.razor`'s edit form still binds single `_editClass`/`_editSubclass`/`_editLevel` locals, and `ConfirmSaveAsync` calls `sheet.SetSingleClass(...)`, which **replaces `Classes` with one entry** — so editing a genuine multiclass hero silently collapses it to a single class on save. It is a latent data-loss footgun and there is no way to *author* a multiclass hero through the UI at all. This slice makes the edit form multiclass-aware and closes the footgun.

## What Changes

- **Domain (tiny):** add `MulticlassRules.KnownClasses` — a public `IReadOnlyList<string>` of the 13 class names, the single source for the class dropdown, kept consistent with the existing prerequisite/proficiency map keys.
- **Edit form:** replace the three single-class inputs with a **repeatable class-row editor** over `_editSheet.Classes` (mirroring the existing Features-list editor): each row is a class `<select>` (from `KnownClasses`) + a level number input + a free-text subclass + a Remove button, plus an "+ Add class" button. Rows bind directly to `_editSheet.Classes[idx]`, so the list *is* the source of truth. Read-only **Total level** and **Prof. bonus** derive live from `_editSheet`. `ConfirmSaveAsync` **drops the `SetSingleClass` call** — the classes are already correct. This is the footgun fix.
- **Validity + proficiency advisory (non-blocking, pure):** computed on the in-memory `_editSheet` via the already-tested `MulticlassRules.CanMulticlassInto` / `MulticlassProficiencies`, recomputed each render. Each non-primary class row shows ✓ allowed or ⚠ "not allowed: {reason}" against the current ability scores, and its reduced multiclass proficiency subset — advisory only, never blocking save. No DB/MCP round-trip; the DB-backed `check_multiclass` MCP tool is untouched.
- **View mode:** list all classes ("Rogue 3 (Thief) / Fighter 2") instead of the single class line.

## Capabilities

### New Capabilities

- `hero-multiclass-editing`: a multiclass-aware Blazor hero editor — a per-class list editor (add/remove class/level/subclass rows) that preserves all classes on save (no `SetSingleClass` collapse), a live derived total level + proficiency bonus, and a non-blocking inline multiclass validity + reduced-proficiency advisory computed on the in-progress sheet; plus the `MulticlassRules.KnownClasses` dropdown source.

## Impact

- **Domain:** `MulticlassRules` gains a public `KnownClasses` list (no behavior change to the existing rules).
- **UI:** `CompanionUI/Components/Pages/Campaigns/HeroDetail.razor` edit + view sections; the `_editClass`/`_editSubclass`/`_editLevel` locals and their `EnterEdit` init are removed; `ConfirmSaveAsync` no longer calls `SetSingleClass`.
- **No change** to the persistence layer, `CharacterResolutionService`, `MulticlassSpellcasting`, the seeded tables, or the MCP tools.
- **Out of scope:** bUnit / Blazor component-test infra; blocking or enforcing multiclass prerequisites on save; spell-slot preview in the UI; any duplicate-class enforcement.
