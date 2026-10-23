import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { PercentPipe } from '@angular/common';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { catchError, combineLatest, distinctUntilChanged, map, of, startWith, switchMap } from 'rxjs';
import { ToastService } from '../../../shared/ui/toast.service';
import { AnalyticsService } from '../data-access/analytics.service';
import { ArenaAnalyticsResponse, DateRange } from '../models/analytics.models';
import { formatMs, formatNumber } from '../shared/format';
import { pipelineLabel } from '../shared/pipelines';

type ArenaState = { status: 'loading' } | { status: 'loaded'; data: ArenaAnalyticsResponse | null };

/** Arena results for the selected period: collapsed by default and requested only once expanded. */
@Component({
  selector: 'app-arena-section',
  imports: [PercentPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './arena-section.component.html',
})
export class ArenaSectionComponent {
  private readonly api = inject(AnalyticsService);
  private readonly toast = inject(ToastService);

  readonly range = input.required<DateRange>();

  protected readonly expanded = signal(false);
  protected readonly selectedPipeline = signal('ALL');
  protected readonly fmt = { number: formatNumber, ms: formatMs, pipeline: pipelineLabel };

  protected readonly state = toSignal(
    combineLatest([toObservable(this.expanded), toObservable(this.range)]).pipe(
      distinctUntilChanged(([e1, r1], [e2, r2]) => e1 === e2 && r1.from === r2.from && r1.to === r2.to),
      switchMap(([expanded, range]) =>
        !expanded
          ? of<ArenaState>({ status: 'loading' })
          : this.api.getArenaAnalytics({ startDate: range.from, endDate: range.to }).pipe(
              map(data => ({ status: 'loaded', data }) as ArenaState),
              startWith<ArenaState>({ status: 'loading' }),
              catchError(() => {
                this.toast.error('No se han podido cargar las métricas de Arena.');
                return of<ArenaState>({ status: 'loaded', data: null });
              }),
            ),
      ),
    ),
    { initialValue: { status: 'loading' } as ArenaState },
  );

  protected readonly analytics = computed(() => {
    const state = this.state();
    return state.status === 'loaded' ? state.data : null;
  });

  protected readonly filteredMetrics = computed(() => {
    const metrics = this.analytics()?.metrics ?? [];
    const pipeline = this.selectedPipeline();
    return pipeline === 'ALL' ? metrics : metrics.filter(m => m.pipeline === pipeline);
  });

  protected readonly availablePipelines = computed(() => (this.analytics()?.metrics ?? []).map(m => m.pipeline));

  protected toggle(): void {
    this.expanded.update(open => !open);
  }
}
