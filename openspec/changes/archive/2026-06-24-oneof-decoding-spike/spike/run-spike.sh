#!/usr/bin/env bash
# Throwaway spike runner for oneof-decoding-spike.
# Exercises Ollama structured-output (`format` = a discriminated-union JSON schema),
# the SAME mechanism Microsoft.Extensions.AI's `ChatResponseFormat.ForJsonSchema` uses
# under the hood (request `format` field -> llama.cpp GBNF grammar).
#
# Requires: Ollama reachable at $OLLAMA_URL with $MODEL pulled. Bring the stack up first
# (e.g. `docker compose up ollama`), then run this from the spike/ directory.
#
# Usage:  ./run-spike.sh            # 3 runs per input, temperature 0
#         RUNS=5 ./run-spike.sh
set -euo pipefail

OLLAMA_URL="${OLLAMA_URL:-http://localhost:11434}"
MODEL="${MODEL:-qwen3:8b}"
RUNS="${RUNS:-3}"
HERE="$(cd "$(dirname "$0")" && pwd)"
SCHEMA="$(cat "$HERE/union-schema.json")"

SYSTEM='You are classifying and extracting D&D rulebook content into ONE entity type. Choose exactly one entityType branch that fits the SOURCE TEXT and fill only that branch'\''s fields. If the text is not a discrete game entity (a heading, table of contents, index, or pure narrative), use entityType "none" with a short reason. The source text may contain OCR artifacts; read through them. Output ONLY the JSON object.'

command -v jq >/dev/null || { echo "jq required"; exit 1; }

# fail fast if Ollama is down
if ! curl -fsS -m 5 "$OLLAMA_URL/api/tags" >/dev/null 2>&1; then
  echo "ERROR: Ollama not reachable at $OLLAMA_URL. Bring the stack up (e.g. 'docker compose up ollama') and retry." >&2
  exit 1
fi

for input in "$HERE"/inputs/*.txt; do
  echo "================================================================"
  echo "INPUT: $(basename "$input")"
  echo "----------------------------------------------------------------"
  USER_TEXT="$(cat "$input")"
  for r in $(seq 1 "$RUNS"); do
    REQ="$(jq -n --arg model "$MODEL" --arg sys "$SYSTEM" --arg user "$USER_TEXT" --argjson schema "$SCHEMA" \
      '{model:$model, stream:false, options:{temperature:0},
        format:$schema,
        messages:[{role:"system",content:$sys},{role:"user",content:$user}]}')"
    RESP="$(curl -fsS -m 300 "$OLLAMA_URL/api/chat" -d "$REQ")"
    CONTENT="$(echo "$RESP" | jq -r '.message.content // empty')"
    echo "run $r: $CONTENT"
  done
  echo
done
echo "Done. Record observations in findings.md (branch chosen, valid JSON / single branch, decline behaviour, any errors)."
