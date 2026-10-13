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

export interface PaginatedBlobsResponse {
  items: BlobInfo[];
  totalCount: number;
  stats: {
    total: number;
    pending: number;
    queued: number;
    processing: number;
    completed: number;
  };
}
