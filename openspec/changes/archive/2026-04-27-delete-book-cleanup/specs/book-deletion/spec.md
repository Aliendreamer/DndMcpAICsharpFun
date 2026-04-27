## ADDED Requirements

### Requirement: A book record can be deleted via the admin API
The system SHALL remove a book's SQLite record, its file from disk, and all associated Qdrant vectors at `DELETE /admin/books/{id}`, returning HTTP 204 on success.

#### Scenario: Completed book is fully deleted
- **WHEN** `DELETE /admin/books/{id}` is called for a `Completed` book
- **THEN** the system deletes all Qdrant vectors for that book, removes the file from disk, deletes the SQLite record, and returns HTTP 204

#### Scenario: Pending book is deleted without touching Qdrant
- **WHEN** `DELETE /admin/books/{id}` is called for a `Pending` book
- **THEN** the system removes the file from disk and deletes the SQLite record without issuing any Qdrant delete, and returns HTTP 204

#### Scenario: Failed book is deleted without touching Qdrant
- **WHEN** `DELETE /admin/books/{id}` is called for a `Failed` book
- **THEN** the system removes the file from disk and deletes the SQLite record without issuing any Qdrant delete, and returns HTTP 204

#### Scenario: Duplicate book is deleted without touching Qdrant
- **WHEN** `DELETE /admin/books/{id}` is called for a `Duplicate` book
- **THEN** the system removes the file from disk and deletes the SQLite record without issuing any Qdrant delete, and returns HTTP 204

#### Scenario: Delete of unknown id returns 404
- **WHEN** `DELETE /admin/books/{id}` is called with an id that does not exist
- **THEN** the system returns HTTP 404

#### Scenario: Delete of in-progress book returns 409
- **WHEN** `DELETE /admin/books/{id}` is called for a book with status `Processing`
- **THEN** the system returns HTTP 409 with a message indicating ingestion is in progress

### Requirement: Qdrant vectors are deleted by reconstructing deterministic point IDs
The system SHALL derive all Qdrant point IDs for a book from its `fileHash` and `chunkCount` and delete them in a single batch, without requiring a payload scan.

#### Scenario: All vectors for a completed book are removed
- **WHEN** `DeleteByHashAsync(fileHash, chunkCount)` is called on the vector store
- **THEN** the system constructs point IDs for indices `0..chunkCount-1` using `SHA256(fileHash + index)[0..16]` and deletes them from Qdrant in one request

### Requirement: Delete ordering protects against partial cleanup
The system SHALL delete Qdrant vectors and the disk file before removing the SQLite record, so that a mid-delete failure leaves the record intact and the operation can be retried.

#### Scenario: Failure before SQLite delete leaves record intact
- **WHEN** Qdrant deletion or disk file removal fails during a delete operation
- **THEN** the SQLite record is NOT deleted and the operation returns an error, allowing the administrator to retry
