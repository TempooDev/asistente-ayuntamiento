import { Injectable, computed, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import {
  ArcElement,
  BarController,
  BarElement,
  CategoryScale,
  ChartOptions,
  DoughnutController,
  Legend,
  LinearScale,
  LineController,
  LineElement,
  PointElement,
  Tooltip,
} from 'chart.js';
import { provideCharts } from 'ng2-charts';
import { ThemeService } from '../../../core/layout/theme.service';
import { pipelineColorVar, pipelineFallbackColor } from './pipelines';

/** Only the Chart.js pieces the analytics page uses; provided by the page so Chart.js stays in its lazy chunk. */
export const provideAnalyticsCharts = () =>
  provideCharts({
    registerables: [
      BarController,
      LineController,
      DoughnutController,
      BarElement,
      LineElement,
      PointElement,
      ArcElement,
      CategoryScale,
      LinearScale,
      Legend,
      Tooltip,
    ],
  });

export interface ChartColors {
  text: string;
  grid: string;
  answers: string;
  latency: string;
  /** Colours of the steps (answer, query expansion, history analysis) in the cost chart. */
  steps: string[];
  pipeline: (name: string) => string;
}

/** Chart colours read from the CSS variables of the current theme (light or dark). */
@Injectable({ providedIn: 'root' })
export class ChartTheme {
  private readonly theme = inject(ThemeService);
  private readonly root = inject(DOCUMENT).documentElement;

  readonly colors = computed<ChartColors>(() => {
    this.theme.theme();
    const style = typeof getComputedStyle === 'function' ? getComputedStyle(this.root) : null;
    const read = (name: string, fallback: string) => style?.getPropertyValue(name).trim() || fallback;
    return {
      text: read('--chart-text', '#43474e'),
      grid: read('--chart-grid', '#e5e7eb'),
      answers: read('--chart-answers', '#455f88'),
      latency: read('--chart-latency', '#dc2626'),
      steps: [read('--chart-answers', '#455f88'), read('--chart-hierarchical', '#d97706'), read('--chart-recursive', '#059669')],
      pipeline: name => {
        const variable = pipelineColorVar(name);
        return variable ? read(variable, pipelineFallbackColor(name)) : pipelineFallbackColor(name);
      },
    };
  });

  /** Common options: themed axes and legend, no animation on theme changes. */
  baseOptions(colors: ChartColors): ChartOptions {
    return {
      responsive: true,
      maintainAspectRatio: false,
      animation: false,
      color: colors.text,
      plugins: { legend: { labels: { color: colors.text } } },
      scales: {
        x: { ticks: { color: colors.text }, grid: { color: colors.grid } },
        y: { ticks: { color: colors.text }, grid: { color: colors.grid }, beginAtZero: true },
      },
    };
  }
}
