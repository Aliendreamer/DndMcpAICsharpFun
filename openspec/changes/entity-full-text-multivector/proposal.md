## Why

Search and chat see only part of most entities. Every renderer except Monster keeps the first paragraph of `fields.entries` (Race, Subrace and Background carry 3% of their words, Condition 4%, Subclass 7%, Class 11%, Feat 27%, Lore and Rule about 50%), and only the first 800 characters are embedded although `mxbai-embed-large` takes 512 tokens. "Demon prince with two heads" ranks Demogorgon 42nd.

## What Changes

- Every type's rendered text carries all of its printed text.
- **BREAKING (index only):** one `dnd_entities` point holds several vectors (Qdrant multivector, MaxSim), one per piece of the text. A Monster's pieces also include its same-named lore. The collection is dropped, recreated and re-ingested (dev environment, no parallel rollout).
- Reranking scores an entity by its best piece.
- `search_dnd` returns up to 3,000 characters per result, around the best piece.
- The chat sets its model context to 8,192 tokens (Ollama's default on this machine is 4,096).
- The embedding cap rises from 800 to 2,000 characters.

## Capabilities

### New Capabilities

- `entity-full-text-retrieval`: pieces, multivector storage, best-piece ranking, result window, chat context.

### Modified Capabilities

- `entity-vector-store`: one point per entity with one vector per piece.
- `structured-entities`: `canonicalText` carries the full text; pieces of it are embedded.
- `fused-reranked-retrieval`: entity search reranks by the best piece.

## Impact

`Features/Entities/CanonicalText`, `Features/VectorStore/Entities`, `Features/Ingestion/Entities`, `Features/Ingestion/FivetoolsIngestion`, `Features/Retrieval`, `Features/Embedding`, `Features/Chat/DndChatService.cs`, `Infrastructure/Qdrant`, `Infrastructure/Ollama`. No endpoint contract changes. `dnd_blocks` is untouched.
