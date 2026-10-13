export enum IngestionStatus {
  Pending = 'Pending',
  Queued = 'Queued',
  Processing = 'Processing',
  Completed = 'Completed',
  Failed = 'Failed',
  Unknown = 'Unknown'
}

export interface BlobInfo {
  name: string;
  size: number;
  lastModified?: string;
  isProcessed: boolean;
  status: IngestionStatus;
}

export interface ProcessResponse {
  message: string;
}

export type PipelineMode = 'BOTH' | 'BASELINE' | 'HIERARCHICAL';

/** Status filter of the document list (values the API accepts). */
export type BlobStatusFilter = 'Todos' | 'Procesados' | 'Encolados' | 'Pendientes';

export interface BlobQuery {
  page: number;
  pageSize: number;
  status: BlobStatusFilter;
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

export interface EnqueueRequest {
  blobPath: string;
  source: string;
}

export interface PaginatedBlobsResponse {
  items: BlobInfo[];
  totalCount: number;
  stats: BlobStats;
}
