## 1. BM25 Sparse Vector Computation

- [x] 1.1 Create `Features/Ingestion/Bm25Vectorizer.cs` — static class with `ComputeBatch(IReadOnlyList<string> texts)` returning `SparseVector[]`; tokenise by lowercasing and splitting on non-alpha; compute TF per document and IDF over the batch; map terms to indices via `Math.Abs(term.GetHashCode()) % 30000`
- [x] 1.2 Create `Features/Ingestion/SparseVector.cs` — record with `int[] Indices` and `float[] Values`, sorted by index ascending (Qdrant requirement)

## 2. Qdrant Collection Initialisation

- [x] 2.1 Add `Qdrant:HybridAlpha` (default `0.5`) to `Config/appsettings.json`
- [x] 2.2 Update `QdrantCollectionInitializer` (or equivalent startup code) to create `dnd_blocks` with a named sparse vector config `"text-sparse"` (`SparseVectorConfig`) alongside the existing dense vector
- [x] 2.3 On startup, detect if existing `dnd_blocks` collection is missing sparse vector support — log a warning (`"dnd_blocks collection has no sparse vector support; hybrid search disabled until re-ingestion"`) and set a boolean flag `_sparseSupported = false`

## 3. Ingestion Pipeline — Sparse Upsert

- [x] 3.1 In the block embedding pipeline, call `Bm25Vectorizer.ComputeBatch` on each batch of block texts immediately after computing dense embeddings
- [x] 3.2 When upserting points to Qdrant, include the `text-sparse` named vector in each point if `_sparseSupported` is true; skip silently if false
- [x] 3.3 Verify upsert payload still contains all existing fields (`text`, `source_book`, `category`, etc.) unchanged

## 4. Hybrid Query

- [x] 4.1 In the retrieval service, compute a BM25 sparse query vector from the user query text using `Bm25Vectorizer.ComputeBatch(new[]{ query })[0]`
- [x] 4.2 When `_sparseSupported` is true, issue a Qdrant `Query` request with both dense prefetch and sparse prefetch fused via RRF, weighted by `HybridAlpha`; when false, issue a standard dense `Search` request
- [x] 4.3 Apply all existing payload filters (`version`, `category`, `sourceBook`, `entityName`, `bookType`) to the hybrid query identically to the current dense-only query
- [x] 4.4 Ensure the result mapping (text, score, payload fields) is unchanged — no callers should need updating

## 5. Tests

- [x] 5.1 Unit test `Bm25Vectorizer` — verify indices are sorted, values are positive, rare terms score higher than common terms, output length matches input batch size
- [x] 5.2 Unit test sparse/dense fallback logic — when `_sparseSupported = false`, query path issues a dense-only search; when true, issues a hybrid query
- [x] 5.3 Run `dotnet build` and `dotnet test` — all tests pass
