## Context

`ExtractionSignatures.IsEntityLikeName` is the name-shape gate consulted by `DeterministicTypeResolver.Resolve`:

```csharp
// Drop non-entity-like names — but never drop a name that is a known 5etools entity.
if (match is null && !ExtractionSignatures.IsEntityLikeName(candidate.DisplayName))
    return TypeResolution.Drop;
```

It rejects step headings, challenge fragments, `^creating`/`features$` section headings, stat-block field labels, sidebar headings, `Appendix*`, structural headers (`ACTIONS`, `TRAITS`, …) and `<X> LAIR`. It has **no** rule for a magic-item stat line, so `"Wondrous item, very rare"` passes the gate. Control then reaches:

```csharp
if (ExtractionSignatures.IsMagicItem(candidate.Text))
    return TypeResolution.Force(EntityType.MagicItem);
```

and the candidate is extracted with a stat line as its name. Candidates that instead hit the decline branch are caught by `RescueAsItemOrNull` / `RescueAsRuleOrNull`, which is why the same junk appears typed `Item` and `Rule`.

The shape is already described in the same file by `MagicItemHeader()`:

```csharp
@"\b(weapon|armou?r|ring|rod|staff|wand|potion|scroll|wondrous)\b[^.\n]{0,40}?,\s*(common|uncommon|rare|very rare|legendary|artifact)\b"
```

There is also an exact precedent for the repair half of this fix. `MonsterStatLineName.Strip` removes a trailing `<Size> <type>` monster stat line and returns the input unchanged when stripping would leave nothing; `EntityNameMatcher.Scan` calls it before normalising for index lookup.

## Goals / Non-Goals

**Goals:**

- Stop emitting entities whose name is a magic-item stat line.
- Preserve real magic items whose heading collapsed together with their stat line, by repairing the name rather than dropping the candidate.
- Keep the emitted entity name clean, not merely the matcher's internal lookup key.
- Mirror the existing monster stat-line mechanism so there is one pattern for this class of defect, not two.

**Non-Goals:**

- Other junk-name shapes in the same output (`"Magic Item Descriptions"` typed `MagicItem`, `"Spell Level Spells"` typed `Lore`, `"Race Option"`, `"Life Span"`). These are generic section headings; no single regex covers them and adding keywords per book is the whack-a-mole pattern this repo has already been burned by.
- Changing which *types* are forced, the decline gate, or the `Item`/`Rule` rescues. Once the name is clean or the candidate is dropped, those paths are correct as written.
- Retro-fixing already-written canonical files. The three affected books are being re-extracted anyway.

## Decisions

**A new `MagicItemStatLineName` type, not another regex inside `ExtractionSignatures`.** The strip is needed by two callers (`EntityNameMatcher` for lookup, `EntityCandidateBuilder` for the emitted name), and `MonsterStatLineName` already establishes the shape of such a helper — same `Strip` signature, same "unchanged if it would empty the name" contract. Putting it in `ExtractionSignatures` would mean a predicate class owning a string transform.

**The strip is anchored on a leading `\s+`, so it only removes a *trailing* stat line.** `"AMULET OF THE DRUNKARD Wondrous item, uncommon"` → `"AMULET OF THE DRUNKARD"`. When the whole name is the stat line there is no preceding whitespace-delimited content, so `Strip` returns the input unchanged and the gate rejects it. This gives the two spec requirements a single implementation with no ambiguity about which applies.

**`IsEntityLikeName` rejects on "the whole name is a stat line", expressed via the strip.** Rather than a second, subtly-different anchored regex, the gate asks whether stripping changed nothing *and* the name matches the stat-line shape. One definition of the shape, no drift between the reject and the repair.

**Order is load-bearing: strip → match → gate.** `DeterministicTypeResolver` deliberately never drops a name that matched a 5etools entry. Repairing the name before the match is therefore what saves a real magic item: `"AMULET OF THE DRUNKARD Wondrous item, uncommon"` matches nothing raw, but matches `Amulet of the Drunkard` once stripped, and a matched candidate is never dropped. If the gate ran before the strip, real items would be dropped rather than repaired. This is recorded in the spec as an explicit constraint because it is invisible from either call site alone.

**`DisplayName` is repaired at candidate-build time.** The matcher's strip only affects lookup; the entity's emitted name comes from the candidate. Repairing in `EntityCandidateBuilder` means every downstream consumer — the id slug, the checkpoint, the extraction prompt — sees the clean name, and there is no path where a clean id pairs with a dirty name.

## Risks / Trade-offs

**A real entity could legitimately end in a rarity phrase and be truncated.** The `MagicItemHeader()` shape requires `<category-token> … , <rarity>` with at most 40 non-period characters between, which no real item name satisfies. The known-good fixture pins `"Ring of Spell Storing"` and similar so a future loosening of the regex fails a test rather than silently truncating names.

**The entity count will fall, and that must not be read as a regression.** EGW should lose ~19 junk entities, and any concatenated name that now grounds to a 5etools entry may merge onto an id that already exists. Validation therefore requires a per-entity diff of what changed, not a count comparison — this repo has previously mistaken a correction (mis-typed backgrounds moving to the right type) for a loss.

**Two callers must both be updated or the fix is half-applied.** If only the matcher strips, ids ground correctly while names stay dirty; if only the builder strips, names are clean but a candidate whose match depended on stripping can still be dropped. The task list keeps both in one task for that reason, and the whole-branch check confirms both paths.

**Unit tests cannot prove this.** The defect was produced by real MinerU output, so the gate is a live re-extract of EGW (the book with the densest magic-item chapter) plus inspection of the resulting names and `declined.json` — not just the fixture.
