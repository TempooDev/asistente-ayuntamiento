export enum IngestionStatus {
  Pending = 'Pending',
  Queued = 'Queued',
  Processing = 'Processing',
  Completed = 'Completed',
  Failed = 'Failed',
  VectorSyncFailed = 'VectorSyncFailed',
  Unknown = 'Unknown'
}

export interface BlobInfo {
  name: string;
  size: number;
  lastModified?: string;
  isProcessed: boolean;
  status: IngestionStatus;
  /** Status in each pipeline; null when that pipeline never received the document. */
  baselineStatus?: IngestionStatus | null;
  hierarchicalStatus?: IngestionStatus | null;
  /** Last ingestion error of the document. */
  errorMessage?: string | null;
}

export interface ProcessResponse {
  message: string;
}

export type PipelineMode = 'BOTH' | 'BASELINE' | 'HIERARCHICAL';

/** Pipeline the status filter applies to; omitted means the summary status. */
export type PipelineFilter = 'BASELINE' | 'HIERARCHICAL';

/** Status filter of the document list (values the API accepts). */
export type BlobStatusFilter = 'Todos' | 'Procesados' | 'Encolados' | 'Pendientes';

export interface BlobQuery {
  page: number;
  pageSize: number;
  status: BlobStatusFilter;
  pipeline?: PipelineFilter | null;
  search?: string;
  dateFrom?: string | null;
  dateTo?: string | null;
  minSizeKb?: number | null;
  maxSizeKb?: number | null;
}

export interface BlobStats {
  total: number;
  pending: number;
  queued: number;
  processing: number;
  completed: number;
}

/** Counters of one pipeline on its own status ("Baseline" or "Hierarchical"). */
export interface PipelineStats {
  pipeline: string;
  total: number;
  completed: number;
  processing: number;
  queued: number;
  failed: number;
  /** Known by the pipeline but not queued (for example after a reset). */
  pending: number;
  notSent: number;
}

export interface EnqueueRequest {
  blobPath: string;
  source: string;
}

export interface PaginatedBlobsResponse {
  items: BlobInfo[];
  /** Filtered count. */
  total: number;
  page: number;
  pageSize: number;
  stats: BlobStats;
  pipelineStats?: PipelineStats[];
}
