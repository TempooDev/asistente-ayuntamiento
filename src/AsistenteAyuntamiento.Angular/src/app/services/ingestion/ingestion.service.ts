import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

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

@Injectable({
  providedIn: 'root'
})
export class IngestionService {
  private http = inject(HttpClient);

  async getBlobs(paramsObj: any = {}): Promise<PaginatedBlobsResponse> {
    let params = new HttpParams();
    Object.keys(paramsObj).forEach(key => {
        if (paramsObj[key] !== null && paramsObj[key] !== undefined && paramsObj[key] !== '') {
            params = params.set(key, paramsObj[key]);
        }
    });

    const result = await firstValueFrom(
      this.http.get<PaginatedBlobsResponse>(`${environment.apiBaseUrl}/api/ingestion/blobs`, { params })
    );
    return result || { items: [], totalCount: 0, stats: { total: 0, pending: 0, processing: 0, completed: 0 } };
  }

  async processBlob(blobPath: string, source: string): Promise<string> {
    const result = await firstValueFrom(
      this.http.post<ProcessResponse>(`${environment.apiBaseUrl}/api/ingestion/process-blob`, { blobPath, source })
    );
    return result?.message ?? "Procesado correctamente.";
  }

  async enqueueBulk(blobs: { blobPath: string, source: string }[], pipelineMode: string = 'BOTH'): Promise<string> {
    const params = new HttpParams().set('pipelineMode', pipelineMode);
    const result = await firstValueFrom(
      this.http.post<ProcessResponse>(`${environment.apiBaseUrl}/api/ingestion/enqueue-bulk`, blobs, { params })
    );
    return result?.message ?? "Documentos encolados correctamente.";
  }

  async resetBlobStatus(documentId: string): Promise<string> {
    const result = await firstValueFrom(
      this.http.post<ProcessResponse>(`${environment.apiBaseUrl}/api/ingestion/reset-status/${documentId}`, {})
    );
    return result?.message ?? "Estado reiniciado correctamente.";
  }

  async resetStuckProcessing(): Promise<string> {
    const result = await firstValueFrom(
      this.http.post<ProcessResponse>(`${environment.apiBaseUrl}/api/ingestion/reset-stuck-processing`, {})
    );
    return result?.message ?? "Documentos colgados reiniciados correctamente.";
  }

  async reprocessAllBlobs(pipelineMode: string = 'BOTH'): Promise<string> {
    const params = new HttpParams().set('pipelineMode', pipelineMode);
    const result = await firstValueFrom(
      this.http.post<ProcessResponse>(`${environment.apiBaseUrl}/api/ingestion/reprocess-all`, {}, { params })
    );
    return result?.message ?? "Reprocesado masivo iniciado correctamente.";
  }
}
