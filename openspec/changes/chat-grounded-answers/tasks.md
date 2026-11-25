## 1. search_entities text

- [x] 1.1 EntityRetrievalService: named results first, text budget, window around the best piece (tests first)
- [x] 1.2 DndMcpTools: drop srd/srd52 from search_entities and list_entities, `text` field, default topK 6

## 2. Lookup after a tool-less answer

- [x] 2.1 DndChatService: look up and ask again (tests first)
- [x] 2.2 ToolCallTrace.ByApp; ModelEval run detail and summary count

## 3. Check

- [x] 3.1 Build, tests, deploy, live check of search_entities text
- [x] 3.2 Reference set run (ask the owner first: about 1-1.5 h GPU), sort what is left

## 4. Page numbers

- [x] 4.1 BookCatalog page offsets; printed page in every retrieval result (tests first)
- [x] 4.2 Persona: a page only from a result's `page` field; the "p. 72" example is gone
- [ ] 4.3 Deploy and check with the evening eval run (ask the owner first: GPU)
