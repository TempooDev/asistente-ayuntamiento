import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { AiMetricsSummary, ArenaAnalyticsRequest, ArenaAnalyticsResponse } from '../models/analytics.models';

/** Admin metrics: Arena win rates per pipeline and AI usage. */
@Injectable({ providedIn: 'root' })
export class AnalyticsService {
  private readonly http = inject(HttpClient);

  getArenaAnalytics(filters?: ArenaAnalyticsRequest): Observable<ArenaAnalyticsResponse> {
    let params = new HttpParams();
    if (filters?.startDate) params = params.set('startDate', filters.startDate);
    if (filters?.endDate) params = params.set('endDate', filters.endDate);
    return this.http.get<ArenaAnalyticsResponse>(`${environment.apiBaseUrl}/api/arena/analytics`, { params });
  }

  getAiMetricsSummary(): Observable<AiMetricsSummary> {
    return this.http.get<AiMetricsSummary>(`${environment.apiBaseUrl}/api/ai/metrics/summary`);
  }
}
