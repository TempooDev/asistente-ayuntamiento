import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface BlobInfo {
  name: string;
  size: number;
  lastModified?: string;
  isProcessed: boolean;
  status: string;
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
      this.http.get<BlobInfo[]>('/api/ingestion/blobs')
    );
    return blobs || [];
  }

  async processBlob(blobPath: string, source: string): Promise<string> {
    const result = await firstValueFrom(
      this.http.post<ProcessResponse>('/api/ingestion/process-blob', { blobPath, source })
    );
    return result?.message ?? "Procesado correctamente.";
  }
}
