import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AiMetricsSummary {
  generatedAtUtc: string;
  totalCalls: number;
  succeededCalls: number;
  failedCalls: number;
  successRate: number;
  averageDurationMs: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalTokens: number;
  averageTokensPerCall: number;
}

@Injectable({
  providedIn: 'root'
})
export class MetricsService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiBaseUrl}/api/ai`;

  getAiMetricsSummary(): Observable<AiMetricsSummary> {
    return this.http.get<AiMetricsSummary>(`${this.apiUrl}/metrics/summary`);
  }
}
