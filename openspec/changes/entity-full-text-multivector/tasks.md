## 1. Full text

- [ ] 1.1 Failing tests, then all renderers carry the full text (`EntriesText`, `RemainingText`)
- [ ] 1.2 Corpus check: every accepted entity's rendered text holds at least 90% of its field words

## 2. Pieces and multivector

- [ ] 2.1 `EntityTextPieces` (split, overlap, heading) with tests
- [ ] 2.2 `EntityPoint` with several vectors; store and collection use multivector; Testcontainers test for MaxSim
- [ ] 2.3 Ingestion, reindex and 5etools import embed pieces; monsters add same-named lore
- [ ] 2.4 Embedding cap 2,000

## 3. Retrieval

- [ ] 3.1 Best-piece reranking in entity and fused search
- [ ] 3.2 `search_dnd` window of 3,000 characters

## 4. Chat

- [ ] 4.1 `Ollama:ChatContextTokens` sent as `num_ctx`

## 5. Proof and rollout (GPU, owner confirms)

- [ ] 5.1 Search test set and chat eval on the old state
- [ ] 5.2 Recreate `dnd_entities`, re-ingest 16 books, counts match
- [ ] 5.3 Search test set and chat eval on the new state; chat speed at 8,192
