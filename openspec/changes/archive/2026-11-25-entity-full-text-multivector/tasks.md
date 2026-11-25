## 1. Full text

- [x] 1.1 Failing tests, then all renderers carry the full text (`EntriesText`, `RemainingText`)
- [x] 1.2 Corpus check: every accepted entity's rendered text holds at least 90% of its field words

## 2. Pieces and multivector

- [x] 2.1 `EntityTextPieces` (split, overlap, heading) with tests
- [x] 2.2 `EntityPoint` with several vectors; store and collection use multivector; Testcontainers test for MaxSim
- [x] 2.3 Ingestion, reindex and 5etools import embed pieces; monsters add same-named lore
- [x] 2.4 Embedding cap 2,000

## 3. Retrieval

- [x] 3.1 Best-piece reranking in entity and fused search
- [x] 3.2 `search_dnd` window of 3,000 characters

## 4. Chat

- [x] 4.1 `Ollama:ChatContextTokens` sent as `num_ctx`

## 5. Proof and rollout (GPU, owner confirms)

- [x] 5.1 Search test set on the old state (the chat eval was not run on it; baseline is the 2026-11-04 report)
- [x] 5.2 Recreate `dnd_entities`, re-ingest 16 books, counts match
- [x] 5.3 Search test set on the new state (MRR 0.468 -> 0.648)
- [x] 5.4 Chat eval on the new state; chat speed at 8,192
  - Done 2026-11-25: exit 0 (no category regressed by more than one case). Shared ids vs 2026-11-04: class -1, others 0; 30 of 102 runs passed (33 before, but the monster and spell cases differ). `search_dnd` was called 0 times in both runs, so the chat eval barely exercises the new search (only 11 `search_entities` calls). Chat at 8,192 tokens: 53 tok/s, same as 4,096, +310 MB, 100% GPU.
