import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  AiMetricsSummary,
  ArenaAnalyticsRequest,
  ArenaAnalyticsResponse,
  DateRange,
  EvaluationRunDetail,
  EvaluationRunSummary,
  IngestionUsage,
} from '../models/analytics.models';

/** Admin metrics: AI usage, ingestion, offline evaluation runs and Arena win rates per pipeline. */
@Injectable({ providedIn: 'root' })
export class AnalyticsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiBaseUrl;

  getArenaAnalytics(filters?: ArenaAnalyticsRequest): Observable<ArenaAnalyticsResponse> {
    let params = new HttpParams();
    if (filters?.startDate) params = params.set('startDate', filters.startDate);
    if (filters?.endDate) params = params.set('endDate', filters.endDate);
    return this.http.get<ArenaAnalyticsResponse>(`${this.baseUrl}/api/arena/analytics`, { params });
  }

  getAiMetricsSummary(range: DateRange): Observable<AiMetricsSummary> {
    return this.http.get<AiMetricsSummary>(`${this.baseUrl}/api/ai/metrics/summary`, { params: rangeParams(range) });
  }

  getIngestionUsage(range: DateRange): Observable<IngestionUsage> {
    return this.http.get<IngestionUsage>(`${this.baseUrl}/api/ai/metrics/ingestion`, { params: rangeParams(range) });
  }

  /** Newest first. */
  getEvaluationRuns(): Observable<EvaluationRunSummary[]> {
    return this.http.get<EvaluationRunSummary[]>(`${this.baseUrl}/api/evaluation/runs`);
  }

  getEvaluationRun(runId: string): Observable<EvaluationRunDetail> {
    return this.http.get<EvaluationRunDetail>(`${this.baseUrl}/api/evaluation/runs/${encodeURIComponent(runId)}`);
  }
}

function rangeParams(range: DateRange): HttpParams {
  return new HttpParams().set('from', range.from).set('to', range.to);
}
