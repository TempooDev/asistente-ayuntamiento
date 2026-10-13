import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { BlobQuery, EnqueueRequest, PaginatedBlobsResponse, PipelineMode, ProcessResponse } from '../models/documents.models';

/** Admin API of the stored documents and their ingestion. Actions return the server's message. */
@Injectable({ providedIn: 'root' })
export class IngestionService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/ingestion`;

  /** Empty filters are left out of the query string. */
  getBlobs(query: BlobQuery): Observable<PaginatedBlobsResponse> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== null && value !== undefined && value !== '') {
        params = params.set(key, String(value));
      }
    }
    return this.http.get<PaginatedBlobsResponse>(`${this.baseUrl}/blobs`, { params });
  }

  processBlob(blobPath: string, source: string): Observable<string> {
    return this.message(this.http.post<ProcessResponse>(`${this.baseUrl}/process-blob`, { blobPath, source }), 'Procesado correctamente.');
  }

  enqueueBulk(blobs: EnqueueRequest[], pipelineMode: PipelineMode = 'BOTH'): Observable<string> {
    const params = new HttpParams().set('pipelineMode', pipelineMode);
    return this.message(this.http.post<ProcessResponse>(`${this.baseUrl}/enqueue-bulk`, blobs, { params }), 'Documentos encolados correctamente.');
  }

  resetBlobStatus(documentId: string): Observable<string> {
    return this.message(this.http.post<ProcessResponse>(`${this.baseUrl}/reset-status/${encodeURIComponent(documentId)}`, {}), 'Estado reiniciado correctamente.');
  }

  resetStuckProcessing(): Observable<string> {
    return this.message(this.http.post<ProcessResponse>(`${this.baseUrl}/reset-stuck-processing`, {}), 'Documentos colgados reiniciados correctamente.');
  }

  reprocessAll(pipelineMode: PipelineMode = 'BOTH'): Observable<string> {
    const params = new HttpParams().set('pipelineMode', pipelineMode);
    return this.message(this.http.post<ProcessResponse>(`${this.baseUrl}/reprocess-all`, {}, { params }), 'Reprocesado masivo iniciado correctamente.');
  }

  private message(request: Observable<ProcessResponse | null>, fallback: string): Observable<string> {
    return request.pipe(map(response => response?.message ?? fallback));
  }
}
