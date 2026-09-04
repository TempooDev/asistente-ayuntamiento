import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/common';
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
  private apiUrl = `${environment.apiUrl}/api/arena`;

  getAnalytics(): Observable<ArenaAnalyticsResponse> {
    return this.http.get<ArenaAnalyticsResponse>(`${this.apiUrl}/analytics`);
  }
}
