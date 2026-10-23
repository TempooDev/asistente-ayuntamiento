## MODIFIED Requirements

### Requirement: Job State Maintenance
The service SHALL let administrators correct document job states: reset one document, repair documents stuck in `Processing`, and reset the baseline data. The summary `Status` SHALL only reflect the production pipelines (`BaselineStatus`, `HierarchicalStatus`); the study statuses (`RecursiveStatus`, `SemanticStatus`) are maintained per pipeline.

#### Scenario: Resetting one document
- **WHEN** an administrator calls `POST /api/ingestion/reset-status/{documentId}`
- **THEN** every pipeline status and the error SHALL be cleared and the summary set to `Pending`
- **AND** an unknown document SHALL answer `404 Not Found`

#### Scenario: Repairing stuck documents
- **WHEN** an administrator calls `POST /api/ingestion/reset-stuck-processing`
- **THEN** each pipeline left in `Processing`, including a study pipeline whose summary does not show it, SHALL become `Completed` if its data exists (`DocumentChunks` of the variant's `ChunkingStrategy` for a flat variant, a `ParentDocument` for hierarchical) and `Pending` otherwise
- **AND** the response SHALL report how many documents ended `Completed` and how many did not

#### Scenario: Resetting the baseline data
- **WHEN** an administrator calls `POST /api/ingestion/reset`
- **THEN** the service SHALL delete the `DocumentChunks` rows with `ChunkingStrategy = FixedOverlap`, drop the Qdrant collection `document_chunks` (recreated on the next ingestion) and invalidate the blob-list cache
- **AND** only the baseline job statuses SHALL be cleared with set-based updates (no row loaded): every `BaselineStatus` set to null, a `[Baseline]` error cleared, and the summary set to the hierarchical status, or `Pending` without one; rows with no other pipeline status SHALL be deleted
- **AND** the study variants' chunks, collections and statuses, and hierarchical data (`ParentDocuments`, `ChildFragments`, `child_fragments`), statuses and errors SHALL be left untouched

#### Scenario: Reset of a document ingested by both pipelines
- **WHEN** a document has `BaselineStatus = Completed` and `HierarchicalStatus = Failed` and the baseline data is reset
- **THEN** its row SHALL remain with `BaselineStatus = null`, `HierarchicalStatus = Failed`, summary `Failed` and its `[Hierarchical]` error

#### Scenario: Reset of a document only in a study variant besides the baseline
- **WHEN** a document has `BaselineStatus = Completed` and `SemanticStatus = Processing` and the baseline data is reset
- **THEN** its row SHALL remain with `BaselineStatus = null`, `SemanticStatus = Processing` and summary `Pending`

## ADDED Requirements

### Requirement: Documents Page Refresh Pauses When Nothing Moves
While some pipeline has documents queued or processing, the documents page SHALL refresh its counters every 10 seconds only as long as the per-pipeline counters keep changing: when a load returns exactly the same per-pipeline counters as the previous one, the periodic refresh SHALL pause, and it SHALL resume as soon as a later load (after a status notification, an action or a filter change) returns different counters.

#### Scenario: A study queue nobody consumes
- **WHEN** the production pipelines are complete and documents wait in a study queue with no study worker running
- **THEN** the page SHALL refresh once more and then stop polling, instead of polling forever

#### Scenario: Ingestion resumes
- **WHEN** the refresh is paused and a status notification or a reload returns different counters while something is still queued or processing
- **THEN** the page SHALL resume refreshing every 10 seconds
