import { ChangeDetectionStrategy, Component, computed, inject, linkedSignal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ChartConfiguration } from 'chart.js';
import { BaseChartDirective } from 'ng2-charts';
import { catchError, map, of, startWith, switchMap } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastService } from '../../../shared/ui/toast.service';
import { AnalyticsService } from '../data-access/analytics.service';
import { EvaluationPipelineResult, EvaluationRunDetail, EvaluationRunSummary } from '../models/analytics.models';
import { ChartTheme } from '../shared/chart-theme';
import { formatMs, formatNumber, formatRatio } from '../shared/format';
import { pipelineLabel } from '../shared/pipelines';

type Metric = { label: string; value: (r: EvaluationPipelineResult) => number | null };

const RAGAS: Metric[] = [
  { label: 'Context precision', value: r => r.contextPrecision },
  { label: 'Context recall', value: r => r.contextRecall },
  { label: 'Faithfulness', value: r => r.faithfulness },
  { label: 'Answer relevancy', value: r => r.answerRelevancy },
];
const RETRIEVAL: Metric[] = [
  { label: 'Hit@1', value: r => r.hitAt1 },
  { label: 'Hit@3', value: r => r.hitAt3 },
  { label: 'Hit@5', value: r => r.hitAt5 },
  { label: 'MRR', value: r => r.mrr },
];
const LATENCY: Metric[] = [
  { label: 'Total p50', value: r => seconds(r.latencyP50Ms) },
  { label: 'Total p95', value: r => seconds(r.latencyP95Ms) },
  { label: 'Primer token p50', value: r => seconds(r.ttftP50Ms) },
  { label: 'Primer token p95', value: r => seconds(r.ttftP95Ms) },
];

function seconds(ms: number | null): number | null {
  return ms == null ? null : ms / 1000;
}

type RunsState = { status: 'loading' } | { status: 'loaded'; runs: EvaluationRunSummary[] } | { status: 'error' };

/** "Evaluación offline": scored runs imported from the evaluation runner. Independent of the period selector. */
@Component({
  selector: 'app-evaluation-section',
  imports: [BaseChartDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './evaluation-section.component.html',
})
export class EvaluationSectionComponent {
  private readonly api = inject(AnalyticsService);
  private readonly toast = inject(ToastService);
  private readonly chartTheme = inject(ChartTheme);

  protected readonly state = toSignal(
    this.api.getEvaluationRuns().pipe(
      map(runs => ({ status: 'loaded', runs }) as RunsState),
      // Optional data: a failure (or an API without the endpoint yet) is shown inline, never as a page error.
      catchError((error: unknown) =>
        of<RunsState>(error instanceof HttpErrorResponse && error.status === 404 ? { status: 'loaded', runs: [] } : { status: 'error' }),
      ),
    ),
    { initialValue: { status: 'loading' } as RunsState },
  );

  protected readonly runs = computed(() => {
    const state = this.state();
    return state.status === 'loaded' ? state.runs : [];
  });

  /** Newest run by default; the user can pick another. */
  protected readonly selectedRunId = linkedSignal(() => this.runs()[0]?.runId ?? null);

  protected readonly run = toSignal(
    toObservable(this.selectedRunId).pipe(
      switchMap(runId =>
        runId === null
          ? of(null)
          : this.api.getEvaluationRun(runId).pipe(
              startWith(null),
              catchError(() => {
                this.toast.error('No se ha podido cargar la evaluación seleccionada.');
                return of(null);
              }),
            ),
      ),
    ),
    { initialValue: null as EvaluationRunDetail | null },
  );

  protected readonly fmt = { number: formatNumber, ms: formatMs, ratio: formatRatio, pipeline: pipelineLabel };

  protected readonly charts = computed(() => [
    { title: 'Métricas RAGAS', config: this.groupedBars(RAGAS, 1) },
    { title: 'Recuperación (hit@k y MRR)', config: this.groupedBars(RETRIEVAL, 1) },
    { title: 'Latencia (s)', config: this.groupedBars(LATENCY, undefined, 's') },
  ]);

  /** Faithfulness (solid) and context recall (dashed) of each pipeline, oldest run first. */
  protected readonly historyChart = computed<ChartConfiguration<'line'> | null>(() => {
    const runs = [...this.runs()].reverse();
    if (runs.length < 2) return null;
    const colors = this.chartTheme.colors();
    const pipelines = [...new Set(runs.flatMap(r => r.results.map(p => p.pipeline)))];
    const value = (run: EvaluationRunSummary, pipeline: string, key: 'faithfulness' | 'contextRecall') =>
      run.results.find(p => p.pipeline === pipeline)?.[key] ?? null;
    const base = this.chartTheme.baseOptions(colors);
    return {
      type: 'line',
      data: {
        labels: runs.map(r => r.runId),
        datasets: pipelines.flatMap(pipeline => [
          {
            label: `${pipelineLabel(pipeline)} · Faithfulness`,
            data: runs.map(r => value(r, pipeline, 'faithfulness')),
            borderColor: colors.pipeline(pipeline),
            backgroundColor: colors.pipeline(pipeline),
            spanGaps: false,
          },
          {
            label: `${pipelineLabel(pipeline)} · Context recall`,
            data: runs.map(r => value(r, pipeline, 'contextRecall')),
            borderColor: colors.pipeline(pipeline),
            backgroundColor: colors.pipeline(pipeline),
            borderDash: [6, 4],
            spanGaps: false,
          },
        ]),
      },
      options: { ...base, scales: { ...base.scales, y: { ...base.scales!['y'], max: 1 } } } as ChartConfiguration<'line'>['options'],
    };
  });

  protected selectRun(runId: string): void {
    this.selectedRunId.set(runId);
  }

  /** One group per metric, one bar per pipeline; missing metrics stay as gaps, never 0. */
  private groupedBars(metrics: Metric[], max?: number, unit?: string): ChartConfiguration<'bar'> | null {
    const results = this.run()?.results ?? [];
    if (results.length === 0) return null;
    const colors = this.chartTheme.colors();
    const base = this.chartTheme.baseOptions(colors);
    return {
      type: 'bar',
      data: {
        labels: metrics.map(m => m.label),
        datasets: results.map(r => ({
          label: pipelineLabel(r.pipeline),
          data: metrics.map(m => m.value(r)),
          backgroundColor: colors.pipeline(r.pipeline),
        })),
      },
      options: {
        ...base,
        scales: {
          ...base.scales,
          y: { ...base.scales!['y'], ...(max !== undefined ? { max } : {}), ...(unit ? { title: { display: true, text: unit, color: colors.text } } : {}) },
        },
      } as ChartConfiguration<'bar'>['options'],
    };
  }
}
