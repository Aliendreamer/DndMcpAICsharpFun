# oneof-decoding-spike — throwaway kit

Answers one go/no-go question for the parent `prose-grounded-knowledge-model` design §D:
**does Ollama/llama.cpp reliably constrain output to a discriminated-union (`oneOf`) schema, and does `qwen3:8b` pick the right branch (or decline)?**

## Contents
- `union-schema.json` — 3 type branches (`Spell`/`Race`/`Monster`, `const` discriminator) + a `{"entityType":"none"}` decline branch.
- `inputs/01-spell-acid-splash.txt` — clean spell (positive control → expect `Spell`).
- `inputs/02-race-draconic-ancestry.txt` — the OCR-noisy Draconic Ancestry block the current pipeline mis-types as `Monster` (expect `Race` or `none`, **never `Monster`**).
- `inputs/03-nonentity-index.txt` — an index passage (decline control → expect `none`).
- `run-spike.sh` — scratch runner. Posts each input to Ollama `/api/chat` with `format=<union-schema>` (the same `format`→GBNF mechanism `ChatResponseFormat.ForJsonSchema` uses), `RUNS` times at temperature 0.
- `findings.md` — fill in after running.

## How to run
Ollama is part of the docker-compose stack and is **not** on the host by default.
```bash
docker compose up -d ollama          # or the full stack
# confirm the model is present (pull if missing):
docker compose exec ollama ollama list   # expect qwen3:8b
cd openspec/changes/oneof-decoding-spike/spike
./run-spike.sh                       # RUNS=5 ./run-spike.sh for more samples
```

## What to record (two SEPARATE questions)
1. **Mechanism** — is each response valid JSON matching exactly ONE branch, with no cross-branch field leakage? (Gates C2 viability.)
2. **Capability** — does the model choose the right branch: spell→`Spell`, Dragonborn→`Race`/`none` (not `Monster`), index→`none`? (Informs whether classifier-as-prior pruning is needed.)

Then write the decision in `findings.md`: **C2 confirmed** / **C2 conditional** (needs pruning) / **C2 rejected** (→ C1 native tools or two-pass router), and update the parent design §D.

> Note: this scratch runner hits the Ollama structured-output endpoint directly (a sanctioned "scratch runner" form per the design). It tests the decoding *mechanism* faithfully. An optional belt-and-suspenders follow-up is to confirm via the real C# `OllamaEntityExtractionClient`, but that is not required for the go/no-go.
