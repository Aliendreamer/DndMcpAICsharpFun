## 1. Prepare the spike harness

- [x] 1.1 Confirm Ollama is running and `qwen3:8b` (the configured `Ollama:ChatModel`) is pulled and responds to a trivial `ForJsonSchema` request — reached the `personalcommandcenter-ollama-1` container at `172.18.0.16:11434` (no host port map); pulled `qwen3:8b`; responds correctly
- [x] 1.2 Author a minimal discriminated-union schema by hand: ~3 branches (`Spell`, `Race`, `Monster`) each with a `const` `entityType` discriminator + a couple of fields, plus a `{"entityType":"none","reason":string}` decline branch — `spike/union-schema.json`
- [x] 1.3 Add a throwaway harness (manual/skipped test or scratch runner) that calls the existing `OllamaEntityExtractionClient`/`IChatClient` with `ChatResponseFormat.ForJsonSchema(unionSchema)` and returns the raw response text — `spike/run-spike.sh` (curl scratch runner hitting Ollama `format`→GBNF, the same mechanism `ForJsonSchema` uses)

## 2. Assemble candidate inputs

- [x] 2.1 Extract the Draconic Ancestry / Dragonborn racial prose from `books/conversion-cache/` (the case C2 must re-type away from `Monster`) — `spike/inputs/02-race-draconic-ancestry.txt` (items 531/532/538, OCR noise preserved)
- [x] 2.2 Pick one cleanly-typed entity passage (e.g. a spell stat block) as a positive control — `spike/inputs/01-spell-acid-splash.txt`
- [x] 2.3 Pick one non-entity passage (a heading / TOC line / narrative paragraph) to test the decline branch — `spike/inputs/03-nonentity-index.txt`

## 3. Run and observe (mechanism vs capability)

- [x] 3.1 Send each candidate a few times; capture raw output, parsed `entityType`, and any grammar/parse errors verbatim — 3 runs/input, temp 0, no errors
- [x] 3.2 Validate MECHANISM: each response is valid JSON conforming to exactly one branch with no cross-branch field leakage — **PASS** (every call)
- [x] 3.3 Validate CAPABILITY: spell → spell branch; Dragonborn prose → race branch or `none` (and NOT `Monster`); non-entity → `none` — **PASS**: spell→`Spell`, Dragonborn→`none` (NOT `Monster`), index→`none`
- [x] 3.4 Record run-to-run consistency (not a single sample) for each case — stable, 3/3 identical per case

## 4. Decide and record

- [x] 4.1 Write `findings.md`: the per-case observations (branch chosen, validity, decline behaviour, errors) — done, with real qwen3:8b output
- [x] 4.2 State the decision — **C2 confirmed** / **C2 conditional** (needs classifier-as-prior pruning) / **C2 rejected** (→ C1 or two-pass router) — with the evidence that drove it — **C2 CONFIRMED**
- [x] 4.3 Note the deferred follow-ups for the C2 milestone (full 22-branch union schema-size/context cost; prompt-phrasing sensitivity) — recorded in `findings.md`
- [x] 4.4 Remove the harness or mark it skipped/manual; update the parent `prose-grounded-knowledge-model` design.md §D with the decision — harness **retained** in `spike/` as the reproducible record (not production code, no build impact); parent §D updated
