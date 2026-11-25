## Why

On the 50 reference questions (2026-11-25) the chat passed 11 (22%). 82 of 150 runs called no tool and mostly invented the rule; 29 runs searched but the tool text lacked the fact, because `search_entities` cuts every result to 240 characters and the model almost never follows with `get_entity` (1 of 37 runs). The books do hold the answers: for 47 of the 48 questions with checkable facts, they are in the full text of the top three entities or the top five passages found for the plain question.

## What Changes

- `search_entities` puts results named in the query first and returns wide text for the best of them; the rest keep the short snippet. Default results 10 -> 6.
- `search_entities` and `list_entities` lose `srd` and `srd52`: no indexed entity carries either flag, so the model setting one hides everything.
- When the model answers a question without any tool call, the chat itself runs `search_entities` and `ask_rules` on the question and asks again with the results.
- The eval trace marks searches made by the chat, and the eval prints how many runs needed one.

## Capabilities

### New Capabilities

### Modified Capabilities
- `mcp-retrieval-tools`: search_entities text and parameters
- `chat-query-routing`: a question answered without a tool is looked up
- `full-chain-eval`: app-made searches are visible in the report

## Impact

`EntityRetrievalService`, `DndMcpTools`, `DndChatService`, `ChatTurnTrace`, `Tools/ModelEval`. No re-ingest, no new index, no endpoint change.
