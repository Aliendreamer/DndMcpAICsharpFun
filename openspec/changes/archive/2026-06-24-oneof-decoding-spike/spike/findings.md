# Findings — oneof-decoding-spike

> **STATUS: RUN COMPLETE (2026-06-24).** Real results from `qwen3:8b` via the PersonalCommandCenter Ollama container. Not fabricated.

## Environment
- Ollama URL: `http://172.18.0.16:11434` (container `personalcommandcenter-ollama-1`, image `ollama/ollama:0.30.9`; reachable on the docker network, no host port map — hence `localhost` failed initially)
- Models: `qwen3:8b` (5.2 GB) for the real run; `qwen3:0.6b` (522 MB) used only for the mechanism smoke test
- Runs per input: 3 (temperature 0)
- Date run: 2026-06-24

## Per-case observations (qwen3:8b)

### 01 — spell (Acid Splash) — expect `Spell`
- Branch chosen: **`Spell`** (3/3)
- Valid JSON / single branch / no cross-branch leakage: **yes** (one run had trailing whitespace, still valid)
- Output: `{"entityType":"Spell","name":"ACID SPLASH","school":"Conjuration","castingTime":"1 action"}`
- Verdict: correct positive.

### 02 — Draconic Ancestry (OCR-noisy) — expect `Race` or `none`, NEVER `Monster`
- Branch chosen: **`none`** (3/3)
- Valid JSON / single branch: **yes**
- Did it avoid `Monster` (the failure C2 targets)? **YES** — and with sound reasoning: *"OCR artifacts… a rule description or table, not a standalone entity… describes a feature's mechanics."*
- Note: declined to `none` rather than `Race`. Defensible — the fixture is the ancestry *table fragment* (items 531/532/538), not the full Dragonborn race block with the race name + traits. Conservative-correct on an ambiguous sub-section; the C2 milestone should confirm a *complete* Race block types as `Race` (not over-decline).

### 03 — index passage — expect `none`
- Branch chosen: **`none`** (3/3)
- Used the decline branch? **yes** — *"an index or cross-reference list… not a discrete game entity."*
- Verdict: correct decline.

## Two-question verdict
- **Mechanism** (decoding constrains to one branch, reliably): **PASS.** Ollama 0.30.9 / llama.cpp constrained the 4-branch discriminated-union (`oneOf` + `const` discriminators) to valid, single-branch JSON in every call. No cross-branch field leakage. No grammar/parse errors.
- **Capability** (model selects correct branch / declines): **PASS.** Spell→`Spell`; index→`none`; Draconic Ancestry→`none` (critically, NOT `Monster`). The decline branch is actively and correctly used — it prevents the fabrication that motivated C2.
- Run-to-run consistency: **stable** (3/3 identical per case at temp 0).
- Ollama errors: **none.**
- Cross-check: the `qwen3:0.6b` smoke test produced valid single-branch JSON too but declined *everything* (incl. the spell) — confirming the mechanism is model-agnostic while branch *selection* needs a capable model.

## Decision
**C2 CONFIRMED** (at representative scale).

Rationale: the spike's load-bearing unknown — *does the reliable grammar-constrained decoding path (`ForJsonSchema` → Ollama `format` → llama.cpp GBNF) handle a discriminated-union `oneOf` with a decline branch?* — is answered **yes**. So C2 (let the model pick its type or decline, under the existing decoding mechanism) is buildable; **no need to fall back to C1 native tool-calling or a two-pass router.** Capability is good and, decisively, the model **declined instead of fabricating a `Monster`** on the exact case that motivated the whole rethink.

## Deferred to the C2 milestone (not in this spike's scope)
- Full ~22-branch union schema size / context cost / selection accuracy on `qwen3:8b` — the classifier-as-prior pruning (parent §F) exists precisely to keep the union small; this result supports that as the right approach.
- Confirm a *complete* Race block (name + traits) types as `Race`, not over-decline to `none`.
- Prompt-phrasing sensitivity on branch selection.
- Optional belt-and-suspenders: reproduce via the real C# `OllamaEntityExtractionClient` path.
