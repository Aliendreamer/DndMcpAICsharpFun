## 1. Extend ModelEval: persona-from-file

- [x] 1.1 Add `PersonaPath` (nullable) to `Tools/ModelEval/EvalArgs.cs` and parse `--persona <path>`.
- [x] 1.2 In `Program.cs`, if `PersonaPath` is set, read the file as the persona; else keep the
  existing hardcoded default string. Unit/behavior check: a no-flag run still uses the default.
- [x] 1.3 Build + a trivial run confirming `--persona` loads a file. Commit.

## 2. Extend ModelEval: symptom adherence checks

- [x] 2.1 Add reusable check helpers: `NoList(text)` (fails if the final prose has numbered `1.`/`2.`
  or `- ` bulleted lines beyond a tolerant threshold) and a `NumberLabel(text, value, correctLabel,
  wrongLabels[])` (fails if the value appears next to a wrong label). Keep them pure + unit-tested.
- [x] 2.2 Wire the new checks into the relevant scenarios (crafting → NumberLabel on the returned
  materials/gold; crafting/npc/rules → NoList) as additional per-run tallies, WITHOUT breaking the
  existing selection/binding/adherence scoring.
- [x] 2.3 Extend `Scorecard` to print the new symptom columns/rows (an N-run tally per scenario) so a
  before/after comparison is legible. Build + run. Commit.

## 3. Baseline measurement

- [x] 3.1 Read the current persona from the running container into `.moe-bench/persona-current.md`
  (`docker exec dndmcpaicsharpfun-app-1 cat /app/Config/personas/companion.md`).
- [x] 3.2 Run the rig with `--persona .moe-bench/persona-current.md --think off --runs 5` against the
  shared Ollama; save the baseline scorecard to `.moe-bench/persona-baseline.txt`. Record the three
  symptom tallies.

## 4. Iterate persona variants

- [x] 4.1 Draft `.moe-bench/persona-v1.md` = current persona + surgical additions: (a) a no-relabel
  numbering directive, (b) a compute-vs-lookup routing either/or, (c) a concrete list→prose exemplar.
  Keep every existing directive.
- [x] 4.2 Run the rig on the variant; diff the symptom tallies vs baseline. If a symptom persists,
  iterate `persona-v2.md` etc. (one lever at a time), stopping at a measured plateau. Save each
  scorecard. Record which edit moved which symptom.

## 5. Land the winner

- [x] 5.1 Present the winning persona text + its before/after scorecard. The USER applies it to
  `Config/personas/companion.md` (git-crypt/permission-protected — agent cannot write it) and rebuilds
  the app image (`docker compose up -d --build app`).
- [x] 5.2 One live chat smoke on a symptom-trigger prompt (e.g. a crafting-number question) confirming
  the tool is selected, the number is labeled correctly, and the answer is prose. Full `dotnet test`
  stays green (ModelEval is a separate console; the app suite is unaffected, but run it to be safe).
- [x] 5.3 Write the change report (baseline vs final symptom tallies + the applied edit + live-smoke
  result). Commit.
