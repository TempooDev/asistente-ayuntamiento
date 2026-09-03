import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

export enum IngestionStatus {
  Pending = 'Pending',
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

@Injectable({
  providedIn: 'root'
})
export class IngestionService {
  private http = inject(HttpClient);

  async getBlobs(): Promise<BlobInfo[]> {
    const blobs = await firstValueFrom(
      this.http.get<BlobInfo[]>(`${environment.apiBaseUrl}/api/ingestion/blobs`)
    );
    return blobs || [];
  }

  async processBlob(blobPath: string, source: string): Promise<string> {
    const result = await firstValueFrom(
      this.http.post<ProcessResponse>(`${environment.apiBaseUrl}/api/ingestion/process-blob`, { blobPath, source })
    );
    return result?.message ?? "Procesado correctamente.";
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
}
