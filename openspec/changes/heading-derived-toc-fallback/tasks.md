> TDD throughout (xUnit + FluentAssertions, warnings-as-errors, Serena for all .cs edits). Run `dotnet test` after each implementation step. Commit per task group.

## 1. Shared keyword category classifier

- [ ] 1.1 Write a failing test `HeadingCategoryClassifierTests` asserting `HeadingCategoryClassifier.Guess(title)` returns the expected `ContentCategory` for representative titles: "Spells"→Spell, "Barbarian"→Class, "Class Features"→Class (not Trait), "Races"→Race, "Rage"→Rule, ""→Rule. (Lock the exact current `GuessCategory` behavior.)
- [ ] 1.2 Create `Features/Ingestion/Pdf/HeadingCategoryClassifier.cs` with `public static ContentCategory Guess(string title)` moved verbatim from `BookmarkTocMapper.GuessCategory` (plus the `Contains`/`ContainsAny` helpers). Run 1.1 → green.
- [ ] 1.3 Refactor `BookmarkTocMapper` to delegate to `HeadingCategoryClassifier.Guess` (keep the parent-title fallback in `BookmarkTocMapper`). Remove the now-duplicated private `GuessCategory`/helpers. Run the existing bookmark/TOC tests + full suite → green (proves the bookmark path is byte-for-byte unchanged).
- [ ] 1.4 Commit: `refactor(extraction): extract shared HeadingCategoryClassifier`.

## 2. HeadingTocMapper (sparse, confident-only)

- [ ] 2.1 Write failing `HeadingTocMapperTests`:
  - **sparse confident emission**: input `section_header` items ["Barbarian"(p1), "Rage"(p2), "Hill Dwarf"(p3), "Spells"(p4)] → `Map` returns entries only for "Barbarian"(Class, StartPage 1) and "Spells"(Spell, StartPage 4); "Rage"/"Hill Dwarf" omitted.
  - **blank title omitted**: a `section_header` with whitespace `Text` produces no entry.
  - **non-entity category omitted**: "Combat" / "Adventuring" headings produce no entry.
  - **propagation via TocCategoryMap**: feeding the mapper's output into `new TocCategoryMap(entries)`, `GetCategory(p3)` (the "Hill Dwarf" page) returns `Class` (inherited from "Barbarian"), and `GetCategory(p4)` returns `Spell`.
- [ ] 2.2 Create `Features/Ingestion/Pdf/HeadingTocMapper.cs`: `public static IReadOnlyList<TocSectionEntry> Map(IReadOnlyList<PdfStructureItem> headings)`. For each item, skip blank `Text`; classify via `HeadingCategoryClassifier.Guess`; emit `new TocSectionEntry(Text.Trim(), category, PageNumber)` **only when** the category is confident (maps to a non-null `EntityType` — Spell/Monster/Class/Race/Background/Item/Condition/God/Plane/Treasure/Trap). Run 2.1 → green.
- [ ] 2.3 Commit: `feat(extraction): HeadingTocMapper for bookmark-less TOC`.

## 3. Orchestrator wiring + corrected log

- [ ] 3.1 In `EntityExtractionOrchestrator.ExtractAsync`, after building `tocMap` from bookmarks: if `tocMap.IsEmpty`, rebuild `tocEntries` from `doc.Items.Where(i => i.Type == "section_header")` via `HeadingTocMapper.Map`, construct a new `TocCategoryMap`, and log an INFO that the heading-derived fallback was used (with heading + emitted-entry counts). Leave the bookmark branch otherwise untouched.
- [ ] 3.2 Update `PdfPigBookmarkReader` so the "falling back to all-categories extraction" message no longer misstates behavior — make it a neutral "no embedded bookmarks found" notice; the orchestrator now owns the accurate fallback log.
- [ ] 3.3 Build (`dotnet build`, 0 warnings) + full suite green.
- [ ] 3.4 Commit: `feat(extraction): wire heading-derived TOC fallback into extraction`.

## 4. Integration + regression coverage

- [ ] 4.1 Write a scanner integration test: given a bookmark-less set of `section_header` + `text` `PdfStructureItem`s, the path `HeadingTocMapper.Map → TocCategoryMap → EntityCandidateScanner.Scan` yields ≥1 typed `EntityCandidate` (e.g. a Class candidate). (Guards the end-to-end no-bookmark path that produced 0 candidates on the SRD.)
- [ ] 4.2 Write a regression test asserting the fallback never fires when bookmarks exist: with a non-empty bookmark-derived `TocCategoryMap`, the orchestrator path does not call `HeadingTocMapper` (assert via the resulting categories matching the bookmark-only expectation, or by structuring the wiring so the heading branch is provably unreached).
- [ ] 4.3 Full suite green; build 0 warnings.
- [ ] 4.4 Commit: `test(extraction): no-bookmark candidate path + bookmark-unaffected regression`.

## 5. Validation

- [ ] 5.1 `openspec validate heading-derived-toc-fallback` passes.
- [ ] 5.2 Confirm no `.http` / `.insomnia.json` change needed (no endpoint surface change) — note explicitly in the final summary.
