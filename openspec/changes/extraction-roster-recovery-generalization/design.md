## Context

`EntityCandidateScanner.Scan` walks section groups and consults the page-keyed `TocCategoryMap`. When the page's `ContentCategory` maps to no `EntityType`, one narrow rescue exists:

```csharp
if (recoverMonsters && matcher is not null &&
    matcher.MatchOfType(s.Section, EntityType.Monster) is { } monster)
{
    var recoveredPrior = ExpandToEntityPrior(ContentCategory.Monster);
    yield return new EntityCandidate(EntityType.Monster, monster.Canonical, s.Text, s.Page, recoveredPrior);
    continue;
}
```

Everything else falls through to a warning and is dropped. `recoverMonsters` is set in `EntityCandidateBuilder` as `record.FivetoolsSourceKey is not null`, so the flag already knows the book is official — it just discards the key itself.

The category is decided by `HeadingCategoryClassifier.Guess`, a keyword ladder over the bookmark title whose fallback is `ContentCategory.Rule`. EGW's character-options chapter title matches none of its keywords, so ~30 pages of races, subraces, subclasses and spells classify as `Rule`, which maps to no `EntityType`.

`EntityNameIndex` already loads spells, classes, subclasses, monsters, items, base items, backgrounds, races, feats, conditions and deities — but not subraces. Entries are `(string Canonical, EntityType Type)`; `EntriesByName` is a multimap retaining one entry per distinct type, first-wins, in load order. Load order is why the untyped `Match` returns `Monster` for `"Orc"`: `bestiary-*` loads before `races.json`.

## Goals / Non-Goals

**Goals:**

- Recover TOC-skipped sections for every indexed entity type, not just `Monster`.
- Type a recovered candidate by the matched entry, so recovery stops mis-typing races as monsters.
- Prefer the book's own 5etools source when a name is ambiguous across sources.
- Index subraces, without which four EGW subraces are unmatchable by any caller.
- Keep cost proportional to roster matches, not to skipped-section count.

**Non-Goals:**

- Fixing `HeadingCategoryClassifier`. Adding `character option` / `subclass` / `lineage` keywords is per-book whack-a-mole, does nothing about mis-typing, and cannot split a chapter whose pages mix races and spells under one category. If this change lands, the `Rule` fallback stops being load-bearing for official books.
- Ungating long runs of skipped sections. ~1096 extra LLM calls for EGW alone, resting entirely on the decline gate to reject adventure prose.
- Changing the confidence bar, the decline gate, or `Match`/`MatchOfType` semantics.
- Recovering types with no `EntityType` (5etools `language`, `reward`, `charoption`). They remain the coverage report's "unmodeled" bucket.

## Decisions

**Type the recovered candidate from the matched entry, not from a fixed category.** The current code hardcodes `EntityType.Monster` and `ExpandToEntityPrior(ContentCategory.Monster)`. The generalized branch takes the match's `Type`, so the prior is derived from what the roster actually says. The union prior still offers the confusion set and frequency floor, so the model can re-type — but it now starts from the right place instead of starting at `Monster` and being expected to climb out. The EGW baseline is the evidence that it does not climb out: `Monster` prior with `Race` in the confusion set still produced `Monster` entities for `"Sea Elf"` and `"Aarakocra"`.

**Same-source preference lives in the matcher, not the scanner.** The scanner should ask "what is this heading, for this book?" and not know about index internals or load order. A new source-aware lookup keeps the ordering rule in the one place that already owns normalisation, fuzzy bounds and tie-breaks. `Match`/`MatchOfType` stay byte-identical so no existing caller shifts.

**Source is added to the entry tuple rather than kept in a parallel map.** `EntriesByName` is already the multimap that retains one entry per distinct type; adding `Source` to the same tuple keeps a single lookup and avoids a second structure that can drift out of sync. This does mean touching every `LoadGlob` call and both dictionaries — a mechanical but wide edit, so it lands as one atomic task (a partially-retyped tuple does not compile).

**Subraces load after base races.** Mirrors the existing documented convention (`// classes — loaded before monsters so "Bard" (class) wins`; subclasses after classes). A name that is both keeps resolving to the broader type under first-wins.

**Recovery stays official-books-only.** The `ungateOnTocFailure` path for keyless books is untouched and remains mutually exclusive with recovery, exactly as the current comment states.

## Risks / Trade-offs

**Precision on generic headings.** Generalizing widens what can match, so an adventure heading could fuzzy-match some item or spell name. Three things bound it: the confidence bar is unchanged (exact, or bounded fuzzy at the existing threshold); same-source preference means EGW headings match EGW content first; and the decline gate still adjudicates every recovered candidate, so a wrong recovery becomes a decline in `declined.json`, not a fabricated entity. Validation must read `declined.json`, not just the entity count.

**Entity ids change for the previously mis-typed sections.** `egw.monster.sea-elf` becomes `egw.subrace.sea-elf`. There is no in-place migration; the three books are re-extracted from scratch. Any consumer holding old ids would break, which is acceptable only because these books have not been ingested yet — that is why the sequencing (fix, re-extract, *then* backfill and ingest) matters.

**Run time grows.** EGW gains an estimated 60–150 candidates (+40–100 min). GGR already has 257 candidates and may grow similarly. This is the intended trade: the alternative is hand-authoring the same entities forever.

**A count rise is not self-evidently good.** The same discipline as the sibling change applies in reverse: confirm *which* entities appeared and that each is real and correctly typed. In particular verify the five known mis-typed EGW names (`Sea Elf`, `Aarakocra`, `Kenku`, `Orc`, `Tortle`) moved from `Monster` to their correct types rather than merely being duplicated.

**Unit tests cannot prove the recovery fires.** A fake matcher proves the branch is wired; only a live re-extract against the real `5etools/` index and real MinerU output proves EGW's chapter is actually recovered. The gate is the live run plus the per-entity diff.
