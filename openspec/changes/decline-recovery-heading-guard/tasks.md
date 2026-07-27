## 1. The guard predicate

- [ ] 1.1 Add `Features/Ingestion/EntityExtraction/RecoveryHeadingGuard.cs` — a `static partial class` with `IsSectionHeading(string? name)` implementing the four rejected shapes from the spec: category-plural suffix (`Creatures|Items|NPCs|Spells|Actions|Lairs|Options|Features|Proficiencies`), bare creature-type plural (whole name is one or two known type plurals, optionally joined by `and`), book-structure heading (`Chapter <n>[: …]`, `Bestiary`, `Appendix …`), and a name that is only `<Name>'s`. Use `[GeneratedRegex]`, matching the file conventions of `ExtractionSignatures` / `MagicItemStatLineName`.
- [ ] 1.2 Fixture test over the REAL observed names — the 97 GGR recoveries are the specification. Assert `true` for a representative set across all four shapes (`Boros Creatures`, `Golgari NPCs`, `Azorius Items`, `Domain Spells`, `Blade Juggler Actions`, `Golgari Lairs`, `Subclass Options`, `Bonus Proficiencies`, `Zombies`, `Aberrations`, `Insects and Arachnids`, `Chapter 5: Treasures`, `Bestiary`, `Aurelia's`, `Trostani's`).
- [ ] 1.3 Negative fixture — assert `false` for everything that must still recover: the 14 kept GGR NPCs (`Teysa Karlov`, `Mizzix of the Izmagnus`, `Prime Speaker Zegana`, `Warleader Aurelia`, `Lazav the Faceless`, `Storrev`, …), genuine recoveries from other books (`Psychic Wind Effects`, `Loose Pantheons`), and the singular-vs-plural discriminator (`Zombie`, `Giant Insect`, `Sphinx of Judgment`). A name that merely ends in `s` must NOT be rejected.

## 2. Wire it into the pass

- [ ] 2.1 In `DeclineRecovery.TryRecoverAsync`, return `null` immediately when `RecoveryHeadingGuard.IsSectionHeading(candidate.DisplayName)` — BEFORE building the prompt or calling the runner, so the LLM call is skipped, not just its result discarded.
- [ ] 2.2 Inject a logger and log each guarded rejection (candidate name + that the recovery was skipped by the name guard), per the spec's traceability requirement. Update the DI registration/ctor call sites in the same task so the build stays 0/0.
- [ ] 2.3 Test at the pass level with a substitute runner: a guarded name returns null AND the runner is NEVER invoked (`ExtractOneAsync` received zero calls) — asserting the saved call, not just the null. An unguarded name still reaches the runner and recovers as before.

## 3. Gates

- [ ] 3.1 `dotnet build` 0 warnings / 0 errors; FULL `dotnet test --filter "FullyQualifiedName!~Persistence"` green.
- [ ] 3.2 `dotnet format DndMcpAICsharpFun.slnx --include <changed files> --verify-no-changes` clean for the changed files only.

## 4. Validation

- [ ] 4.1 Replay the real GGR names through the predicate offline: all 90 removed names classify as headings, and all 15 kept names do not. This is the cheap deterministic harness — no re-extract needed to prove the predicate.
- [ ] 4.2 Confirm the guard cannot regress the other two books: replay EGW's and SCC's decline-recovery names (10 and 12) and check which the guard would now suppress; any that are genuine must NOT be caught, or the vocabulary needs narrowing.
- [ ] 4.3 Record the replay result in the change folder as evidence.
