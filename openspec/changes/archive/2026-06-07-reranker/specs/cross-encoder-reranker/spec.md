## ADDED Requirements
### Requirement: ONNX cross-encoder model downloaded at startup
The system SHALL check for the ONNX model file at `{Reranker:ModelPath}/model.onnx` on startup. If the file does not exist and `Reranker:Enabled` is true, the system SHALL download it from the configured `Reranker:ModelUrl` using `HttpClient`, logging progress. If the download fails, the system SHALL log a warning, set reranking to disabled for the session, and continue startup normally. The system SHALL never fail startup due to a missing or undownloadable model.

#### Scenario: Model downloaded on first startup

- **WHEN** the application starts and `model.onnx` does not exist in the models volume
- **THEN** the file is downloaded, saved to the models volume, and the reranker is initialised

#### Scenario: Cached model loaded on subsequent startups

- **WHEN** the application starts and `model.onnx` already exists in the models volume
- **THEN** the model is loaded directly without downloading

#### Scenario: Download failure disables reranking gracefully

- **WHEN** the application starts and the model download fails
- **THEN** a warning is logged, reranking is disabled for the session, and the application starts normally

### Requirement: Cross-encoder scores (query, passage) pairs
The system SHALL expose a `RerankAsync(string query, IReadOnlyList<string> passages, CancellationToken ct)` method returning scores in the same order as the input passages. Each passage SHALL be tokenized as `[CLS] query [SEP] passage [SEP]` using BERT WordPiece tokenization. Texts exceeding 512 tokens SHALL be truncated to fit. Scores SHALL be raw logits from the ONNX model output (higher = more relevant).

#### Scenario: Scores returned in input order

- **WHEN** `RerankAsync` is called with 3 passages
- **THEN** 3 scores are returned in the same order as the input passages

#### Scenario: Higher score for more relevant passage

- **WHEN** `RerankAsync` is called with a query "What does Fireball do" and passages containing the Fireball spell description and an unrelated passage
- **THEN** the Fireball passage receives a higher score

#### Scenario: Long passages truncated without error

- **WHEN** a passage exceeds 512 tokens after tokenization
- **THEN** it is truncated to 510 tokens and scored without throwing

### Requirement: Reranker selects TopN from TopK candidates
The system SHALL rerank the top-`Reranker:TopK` candidates retrieved from Qdrant and return the top-`Reranker:TopN` highest-scoring passages to callers, sorted by reranker score descending. When `Reranker:Enabled` is false or the model failed to load, the system SHALL return the first TopN candidates from the Qdrant results ordered by Qdrant score, with no reranker call.

#### Scenario: TopN returned after reranking

- **WHEN** Qdrant returns 20 candidates and `Reranker:TopN` is 5
- **THEN** 5 passages are returned, ordered by cross-encoder score

#### Scenario: Fallback when reranker disabled

- **WHEN** `Reranker:Enabled` is false
- **THEN** the first TopN candidates by Qdrant score are returned without calling the reranker
