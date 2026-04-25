## ADDED Requirements

### Requirement: Background service runs ingestion on startup then every 24 hours
The system SHALL run one full ingestion pass immediately when the application starts, then repeat every 24 hours.

#### Scenario: First pass runs on startup without waiting
- **WHEN** the application starts and there are Pending books registered
- **THEN** ingestion of those books begins within seconds of startup, not after a 24-hour wait

#### Scenario: Subsequent passes run every 24 hours
- **WHEN** an ingestion pass completes
- **THEN** the next pass is scheduled 24 hours later

### Requirement: Background service processes Pending and Failed records
The system SHALL include both `Pending` and `Failed` records in each ingestion pass.

#### Scenario: Failed records are retried on next cycle
- **WHEN** a record has `status = Failed` at the start of an ingestion pass
- **THEN** the service attempts ingestion again for that record

#### Scenario: Completed records are skipped
- **WHEN** a record has `status = Completed` and its file hash is unchanged
- **THEN** the service skips that record without re-processing

### Requirement: Background service marks records as Processing before starting
The system SHALL set a record's `status` to `Processing` before beginning extraction, preventing concurrent duplicate processing.

#### Scenario: Record is marked Processing before extraction begins
- **WHEN** the background service picks up a Pending record
- **THEN** the status is set to `Processing` in SQLite before any PDF bytes are read

### Requirement: Background service logs a structured summary after each pass
The system SHALL emit a structured log entry after each pass with counts of: processed, skipped, failed, and total registered books.

#### Scenario: Pass summary is logged
- **WHEN** an ingestion pass completes
- **THEN** a structured log entry is emitted with `processed`, `skipped`, `failed`, and `total` counts
