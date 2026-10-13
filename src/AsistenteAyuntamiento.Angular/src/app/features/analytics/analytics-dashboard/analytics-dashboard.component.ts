import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe, PercentPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Observable, catchError, of, switchMap } from 'rxjs';
import { AuthFacade } from '../../../core/auth/auth-facade.service';
import { ToastService } from '../../../shared/ui/toast.service';
import { AnalyticsService } from '../data-access/analytics.service';
import { AiMetricsSummary, ArenaAnalyticsRequest, ArenaAnalyticsResponse, IngestionUsage } from '../models/analytics.models';

/** Admin metrics: AI usage of the organization; Arena results per pipeline (filterable by date) and ingestion cost of the platform. */
@Component({
  selector: 'app-analytics-dashboard',
  imports: [FormsModule, DecimalPipe, PercentPipe],
  templateUrl: './analytics-dashboard.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AnalyticsDashboardComponent {
  private readonly api = inject(AnalyticsService);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthFacade);

  /** Usage of the caller's organization. */
  readonly showUsage = this.auth.isOrganizationAdmin;
  /** Arena results of every organization (platform data). */
  readonly showArena = this.auth.isPlatformAdmin;
  /** Ingestion cost of the whole platform (bulletins are shared). */
  readonly showIngestion = this.auth.isPlatformAdmin;

  readonly startDate = signal('');
  readonly endDate = signal('');
  readonly selectedPipeline = signal('ALL');

  /** A failed request shows a toast and leaves the section empty instead of breaking the page. */
  readonly aiMetrics = toSignal(
    (this.showUsage() ? this.api.getAiMetricsSummary() : of(null) as Observable<AiMetricsSummary | null>).pipe(
      catchError(() => {
        this.toast.error('No se han podido cargar las métricas de IA.');
        return of(null);
      }),
    ),
    { initialValue: null },
  );

  readonly ingestionUsage = toSignal(
    (this.showIngestion() ? this.api.getIngestionUsage() : (of(null) as Observable<IngestionUsage | null>)).pipe(
      catchError(() => {
        this.toast.error('No se han podido cargar las métricas de ingesta.');
        return of(null);
      }),
    ),
    { initialValue: null },
  );

  private readonly filters = computed<ArenaAnalyticsRequest>(() => ({ startDate: this.startDate(), endDate: this.endDate() }));

  readonly analytics = toSignal(
    toObservable(this.filters).pipe(
      switchMap(filters =>
        (this.showArena() ? this.api.getArenaAnalytics(filters) : (of(null) as Observable<ArenaAnalyticsResponse | null>)).pipe(
          catchError(() => {
            this.toast.error('No se han podido cargar las métricas de Arena.');
            return of<ArenaAnalyticsResponse | null>(null);
          }),
        ),
      ),
    ),
    { initialValue: null },
  );

  readonly filteredMetrics = computed(() => {
    const metrics = this.analytics()?.metrics ?? [];
    const pipeline = this.selectedPipeline();
    return pipeline === 'ALL' ? metrics : metrics.filter(m => m.pipeline === pipeline);
  });

  readonly availablePipelines = computed(() => (this.analytics()?.metrics ?? []).map(m => m.pipeline));
}
