import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Observable, catchError, distinctUntilChanged, forkJoin, map, of, startWith, switchMap } from 'rxjs';
import { AuthFacade } from '../../../core/auth/auth-facade.service';
import { ToastService } from '../../../shared/ui/toast.service';
import { ArenaSectionComponent } from '../arena-section/arena-section.component';
import { AnalyticsService } from '../data-access/analytics.service';
import { EvaluationSectionComponent } from '../evaluation-section/evaluation-section.component';
import { IngestionSectionComponent } from '../ingestion-section/ingestion-section.component';
import { DateRange, IngestionUsage, AiMetricsSummary } from '../models/analytics.models';
import { provideAnalyticsCharts } from '../shared/chart-theme';
import { PeriodPreset, PeriodSelection, Periods, isoDate, resolvePeriods } from '../shared/period';
import { UsageSectionComponent } from '../usage-section/usage-section.component';

/** A section's data for the current period and the previous one; `loading` until the current one answers. */
interface Comparison<T> {
  loading: boolean;
  current: T | null;
  previous: T | null;
}

const LOADING = { loading: true, current: null, previous: null };

/**
 * Admin metrics page: one period selector for the organization usage, the platform ingestion and the Arena;
 * the offline evaluation has its own run selector. Each section checks its own role.
 */
@Component({
  selector: 'app-analytics-dashboard',
  imports: [UsageSectionComponent, IngestionSectionComponent, EvaluationSectionComponent, ArenaSectionComponent],
  providers: [provideAnalyticsCharts()],
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
  /** Ingestion of the whole platform (bulletins are shared). */
  readonly showIngestion = this.auth.isPlatformAdmin;
  /** Offline evaluation runs (platform data). */
  readonly showEvaluation = this.auth.isPlatformAdmin;

  protected readonly presets: readonly { value: PeriodPreset; label: string }[] = [
    { value: '7d', label: 'Últimos 7 días' },
    { value: '30d', label: 'Últimos 30 días' },
    { value: 'custom', label: 'Personalizado' },
  ];
  protected readonly today = isoDate(new Date());
  protected readonly period = signal<PeriodSelection>({ preset: '30d', start: '', end: '' });

  /** Keeps the last valid periods while a custom range is being typed. */
  private lastPeriods: Periods = resolvePeriods({ preset: '30d', start: '', end: '' })!;
  protected readonly periods = computed<Periods>(() => {
    const resolved = resolvePeriods(this.period());
    if (resolved) this.lastPeriods = resolved;
    return this.lastPeriods;
  });
  protected readonly invalidCustomRange = computed(() => this.period().preset === 'custom' && resolvePeriods(this.period()) === null);

  private readonly periods$ = toObservable(this.periods).pipe(
    distinctUntilChanged((a, b) => a.current.from === b.current.from && a.current.to === b.current.to),
  );

  readonly usage = toSignal(
    this.compare(this.showUsage(), range => this.api.getAiMetricsSummary(range), 'No se han podido cargar las métricas de IA.'),
    { initialValue: LOADING as Comparison<AiMetricsSummary> },
  );

  readonly ingestion = toSignal(
    this.compare(this.showIngestion(), range => this.api.getIngestionUsage(range), 'No se han podido cargar las métricas de ingesta.'),
    { initialValue: LOADING as Comparison<IngestionUsage> },
  );

  protected selectPreset(preset: string): void {
    this.period.update(p => {
      const next = { ...p, preset: preset as PeriodPreset };
      if (next.preset === 'custom' && !next.start && !next.end) {
        // Start from the period shown so far.
        const { current } = this.periods();
        next.start = current.from;
        next.end = isoDate(new Date(Date.parse(`${current.to}T00:00:00Z`) - 86_400_000));
      }
      return next;
    });
  }

  protected setCustom(change: Partial<Pick<PeriodSelection, 'start' | 'end'>>): void {
    this.period.update(p => ({ ...p, ...change }));
  }

  /**
   * Requests a section for the current and the previous period each time the period changes.
   * A failed current request shows a toast and leaves the section empty; a failed previous one only hides the change.
   */
  private compare<T>(enabled: boolean, load: (range: DateRange) => Observable<T>, error: string): Observable<Comparison<T>> {
    if (!enabled) return of({ loading: false, current: null, previous: null });
    return this.periods$.pipe(
      switchMap(({ current, previous }) =>
        forkJoin({
          current: load(current).pipe(
            catchError(() => {
              this.toast.error(error);
              return of(null);
            }),
          ),
          previous: load(previous).pipe(catchError(() => of(null))),
        }).pipe(
          map(result => ({ loading: false, ...result })),
          startWith(LOADING as Comparison<T>),
        ),
      ),
    );
  }
}
