import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface ArenaCompareRequest {
  query: string;
}

export interface ArenaCompareResponse {
  sessionId: string;
  optionAlfa: string;
  optionBeta: string;
  latencyAlfaMs: number;
  latencyBetaMs: number;
  sourcesAlfa: string[];
  sourcesBeta: string[];
}

export interface ArenaVoteRequest {
  sessionId: string;
  winner: string; // 'ALFA', 'BETA', 'TIE', 'BOTH_BAD'
  clarityReason?: string;
  precisionReason?: string;
  optionalComment?: string;
}

export interface ArenaVoteResponse {
  alfaSystem: string;
  betaSystem: string;
}

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
  providedIn: 'root',
})
export class ArenaService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiBaseUrl}/api/arena`;

  compare(request: ArenaCompareRequest) {
    return this.http.post<ArenaCompareResponse>(`${this.apiUrl}/compare`, request);
  }

  vote(request: ArenaVoteRequest) {
    return this.http.post<ArenaVoteResponse>(`${this.apiUrl}/vote`, request);
  }

  getAnalytics(filters?: ArenaAnalyticsRequest): Observable<ArenaAnalyticsResponse> {
    let params = new HttpParams();
    if (filters?.startDate) params = params.set('startDate', filters.startDate);
    if (filters?.endDate) params = params.set('endDate', filters.endDate);

    return this.http.get<ArenaAnalyticsResponse>(`${this.apiUrl}/analytics`, { params });
  }
}
