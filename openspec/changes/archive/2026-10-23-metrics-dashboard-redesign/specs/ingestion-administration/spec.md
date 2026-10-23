## MODIFIED Requirements

### Requirement: Blob Listing and Inspection
`ListBlobsAsync` SHALL list the blobs under `json/` combined with their `DocumentJobState`: the summary `Status` (`Pending` when there is none) and the status of each pipeline (`BaselineStatus`, `HierarchicalStatus`, null when that pipeline never received the document) with the last error. It SHALL cache that combined list in memory for 10 seconds, and filter and paginate it in memory. The documents page SHALL show one progress bar per pipeline (completed, processing, queued, failed and not sent, out of the total documents), one status column per pipeline in the table with the error as a tooltip, and SHALL refresh the counters and the visible page every 10 seconds while any document is queued or processing in some pipeline. The status filter SHALL default to "Todos".

#### Scenario: Listing documents with filters
- **WHEN** an administrator calls `GET /api/ingestion/blobs` with `search`, `status`, `pipeline`, `dateFrom`, `dateTo`, `minSizeKb`, `maxSizeKb`, `page` and `pageSize`
- **THEN** the result SHALL contain the blobs whose path contains `search` (case-insensitive), whose status matches the tab (`Procesados` = Completed; `Pendientes` = Pending, Failed or VectorSyncFailed; `Encolados` = Queued or Processing; `Todos` or empty = all), evaluated on the given `pipeline` (`BASELINE` or `HIERARCHICAL`) or on the summary status when `pipeline` is empty, whose `LastModified` date is within the range and whose size is within the range
- **AND** return `total` (filtered count), `page`, `pageSize` (default 100) and the page of `items`, each with its summary status and its per-pipeline statuses

#### Scenario: Counters
- **WHEN** the list is returned
- **THEN** `stats` SHALL count all listed blobs (not only the filtered ones) as total, pending (Pending, Failed, VectorSyncFailed), queued, processing and completed on the summary status
- **AND** `pipelineStats` SHALL give the same counts for each pipeline on its own status, plus `failed` and `notSent` (no status for that pipeline)

#### Scenario: One pipeline ahead of the other
- **GIVEN** 201 documents, 150 completed by the baseline pipeline and 3 by the hierarchical one
- **WHEN** an administrator opens the documents page
- **THEN** the baseline bar SHALL show 150 of 201 completed and the hierarchical bar 3 of 201, instead of a single "queued" count for both

#### Scenario: Progress while ingesting
- **WHEN** some document is queued or processing in any pipeline
- **THEN** the page SHALL refresh the bars and the visible rows every 10 seconds, and SHALL stop refreshing when nothing is queued or processing

#### Scenario: Bucket unavailable
- **WHEN** listing the bucket fails
- **THEN** the error SHALL be logged and the blobs read so far returned (an empty list if none)
