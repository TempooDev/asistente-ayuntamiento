## ADDED Requirements

### Requirement: Zero-Cost Vector Migration Script
The system SHALL provide a utility to migrate existing embedded fragments from PostgreSQL to Qdrant without invoking any external embedding LLM API.

#### Scenario: Running the migration utility
- **WHEN** an administrator triggers the migration utility
- **THEN** the system SHALL stream existing `ChildFragment` records from PostgreSQL, extract the pre-calculated 4096-dimensional vectors, and bulk-insert them into the Qdrant collection
- **AND** log the number of records migrated successfully.
