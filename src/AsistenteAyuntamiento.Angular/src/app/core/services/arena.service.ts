import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface PipelineMetrics {
  pipeline: string;
  winCount: number;
  lossCount: number;
  tieCount: number;
  winRate: number;
  averageLatencyMs: number;
  averageTokens: number;
}

export interface ArenaAnalyticsRequest {
  startDate?: string;
  endDate?: string;
}

export interface ArenaAnalyticsResponse {
  totalBattles: number;
  pendingBattles: number;
  completedBattles: number;
  metrics: PipelineMetrics[];
}

@Injectable({
  providedIn: 'root'
})
export class ArenaService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiBaseUrl}/api/arena`;

  getAnalytics(filters?: ArenaAnalyticsRequest): Observable<ArenaAnalyticsResponse> {
    let params = new HttpParams();
    if (filters?.startDate) params = params.set('startDate', filters.startDate);
    if (filters?.endDate) params = params.set('endDate', filters.endDate);

    return this.http.get<ArenaAnalyticsResponse>(`${this.apiUrl}/analytics`, { params });
  }
}

