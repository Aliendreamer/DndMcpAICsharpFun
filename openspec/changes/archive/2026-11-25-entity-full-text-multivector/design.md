## Context

`mxbai-embed-large` reads 512 tokens; 800 characters of our text use 180-206. A spike on 325 monsters (rank of the right answer): raising the limit alone moved "demon prince with two heads" from 42 to 29; lore-first text moved it to 1 but broke rules searches ("turns people to stone" 4 to 129); keeping both vectors was the best compromise.

## Goals / Non-Goals

**Goals:** all printed text searchable and returned; the best part of an entity decides its rank.

**Non-Goals:** `dnd_blocks`, a different embedding or chat model, a parallel-collection rollout (dev environment; owner: "just do it").

## Decisions

1. **Full text in the renderers.** The simple renderers use all of `entries` (`RendererHelpers.EntriesText`); Class, Subclass, Object, Spell and Trap also append the text of any field they do not already show (`RendererHelpers.RemainingText`). Openings stay as they are.
2. **Pieces.** `EntityTextPieces.Split`: up to 1,500 characters, cut at a line, sentence or space, 150 characters overlap; each piece after the first is headed "Name (Type): ". Notes join the text. Embedding cap 2,000 characters, `Truncate = true` stays as the guard for dense text.
3. **Monster lore.** Pieces of the Accepted Lore entities of the same book with the same normalised name are added to the Monster's vectors only, never to its `canonicalText`.
4. **Multivector.** `VectorParams.MultivectorConfig = MaxSim` on the entities collection. Point ids, counts and payload are unchanged. Alternatives rejected: several points per entity (breaks ids, counts, deletes, filters), one longer vector (1,493 entries still cut).
5. **Best piece.** Qdrant does not say which piece matched, so the reranker does: for each candidate the first piece and the three with the most query words are scored, the best score ranks it. At most four passages per candidate.
6. **Result window.** `search_dnd` returns the whole text when it is at most 3,000 characters, else a 3,000-character window around the best piece.
7. **Chat context** `Ollama:ChatContextTokens` = 8192, sent as `num_ctx`. Measured after deployment; lowered if the chat becomes too slow on 8 GB.

## Risks / Trade-offs

- [VRAM: 8,192 tokens may push qwen3 partly to the CPU] -> measure, config value.
- [Slower search: up to 4 passages per candidate] -> bounded.
- [Grounding code also embeds through the same service and its 800 cut] -> only the legacy LLM route uses it; all 16 books are programmatic.

## Migration Plan

Delete `dnd_entities`, start the new app (it recreates the collection as multivector), run `apply-overrides` for the 16 books. Rollback: check out the previous commit and do the same.
