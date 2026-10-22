import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { ChartConfiguration } from 'chart.js';
import { BaseChartDirective } from 'ng2-charts';
import { AiMetricsSummary } from '../models/analytics.models';
import { ChartTheme } from '../shared/chart-theme';
import { formatEur, formatMs, formatNumber, formatPercent } from '../shared/format';
import { MetricCardComponent } from '../shared/metric-card.component';
import { percentChange } from '../shared/period';
import { pipelineLabel } from '../shared/pipelines';

/** "Uso del asistente": cards with the change against the previous period, daily chart, per-pipeline table and cost by step. */
@Component({
  selector: 'app-usage-section',
  imports: [BaseChartDirective, MetricCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './usage-section.component.html',
})
export class UsageSectionComponent {
  private readonly chartTheme = inject(ChartTheme);

  readonly summary = input.required<AiMetricsSummary>();
  readonly previous = input<AiMetricsSummary | null>(null);

  protected readonly fmt = { number: formatNumber, ms: formatMs, eur: formatEur, percent: formatPercent, pipeline: pipelineLabel };

  protected readonly changes = computed(() => {
    const cur = this.summary();
    const prev = this.previous();
    return {
      answers: percentChange(cur.totalCalls, prev?.totalCalls),
      p95: percentChange(cur.latencyP95Ms, prev?.latencyP95Ms),
      ttft: percentChange(cur.ttftP50Ms, prev?.ttftP50Ms),
      tokens: percentChange(cur.averageTokensPerCall, prev?.averageTokensPerCall),
      cost: percentChange(cur.estimatedCostEur, prev?.estimatedCostEur),
    };
  });

  protected readonly dailyChart = computed<ChartConfiguration<'bar' | 'line'>>(() => {
    const colors = this.chartTheme.colors();
    const daily = this.summary().daily;
    const base = this.chartTheme.baseOptions(colors);
    return {
      type: 'bar',
      data: {
        labels: daily.map(d => d.date.slice(5)),
        datasets: [
          { type: 'bar', label: 'Respuestas', data: daily.map(d => d.answers), backgroundColor: colors.answers, yAxisID: 'y', order: 2 },
          {
            type: 'line',
            label: 'Latencia p95 (s)',
            data: daily.map(d => (d.latencyP95Ms == null ? null : d.latencyP95Ms / 1000)),
            borderColor: colors.latency,
            backgroundColor: colors.latency,
            yAxisID: 'y1',
            spanGaps: false,
            order: 1,
          },
        ],
      },
      options: {
        ...base,
        scales: {
          x: base.scales!['x'],
          y: { ...base.scales!['y'], title: { display: true, text: 'Respuestas', color: colors.text }, ticks: { color: colors.text, precision: 0 } },
          y1: {
            position: 'right',
            beginAtZero: true,
            title: { display: true, text: 'p95 (s)', color: colors.text },
            ticks: { color: colors.text },
            grid: { drawOnChartArea: false },
          },
        },
      } as ChartConfiguration<'bar' | 'line'>['options'],
    };
  });

  protected readonly costChart = computed<ChartConfiguration<'doughnut'>>(() => {
    const colors = this.chartTheme.colors();
    const steps = this.summary().costByStep;
    return {
      type: 'doughnut',
      data: {
        labels: steps.map(s => s.step),
        datasets: [{ data: steps.map(s => s.costEur), backgroundColor: steps.map((_, i) => colors.steps[i % colors.steps.length]), borderColor: colors.grid }],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        plugins: {
          legend: { position: 'bottom', labels: { color: colors.text } },
          tooltip: { callbacks: { label: item => `${item.label}: ${formatEur(item.raw as number)}` } },
        },
      },
    };
  });

  protected readonly hasCost = computed(() => this.summary().costByStep.some(s => s.costEur > 0));
}
