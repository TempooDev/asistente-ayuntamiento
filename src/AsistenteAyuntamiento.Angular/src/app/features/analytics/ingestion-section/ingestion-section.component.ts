import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { IngestionUsage } from '../models/analytics.models';
import { ChartTheme } from '../shared/chart-theme';
import { formatMs, formatNumber } from '../shared/format';
import { percentChange } from '../shared/period';
import { pipelineLabel } from '../shared/pipelines';

/** "Ingesta": one column per pipeline, each document counted once (its latest ingestion). */
@Component({
  selector: 'app-ingestion-section',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4 mb-8">
      @for (p of columns(); track p.pipeline) {
        <div class="bg-surface-container-lowest p-5 rounded-xl shadow-xs border border-gray-100 border-t-4" [style.border-top-color]="p.color" data-testid="ingestion-pipeline">
          <h3 class="text-lg font-bold text-gray-800 mb-3">{{ p.label }}</h3>
          <dl class="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
            <dt class="text-gray-500">Documentos</dt>
            <dd class="text-right font-bold text-gray-900">
              {{ fmt.number(p.documents) }}
              @if (p.change !== null) {
                <span class="block text-xs font-normal text-gray-500">{{ p.change > 0 ? '+' : '' }}{{ fmt.number(p.change, 1) }} % vs periodo anterior</span>
              }
            </dd>
            <dt class="text-gray-500">Tiempo por documento p50 / p95</dt>
            <dd class="text-right">{{ fmt.ms(p.processingP50Ms) }} / {{ fmt.ms(p.processingP95Ms) }}</dd>
            <dt class="text-gray-500">Chunks por documento</dt>
            <dd class="text-right">{{ fmt.number(p.averageChunks, 1) }}</dd>
            <dt class="text-gray-500">Tokens de embeddings</dt>
            <dd class="text-right">{{ fmt.number(p.embeddedTokens) }}</dd>
            <dt class="text-gray-500">Llamadas de enriquecimiento</dt>
            <dd class="text-right">{{ fmt.number(p.enrichmentCalls) }}</dd>
            <dt class="text-gray-500">Tokens de enriquecimiento</dt>
            <dd class="text-right">{{ fmt.number(p.enrichmentTokens) }}</dd>
            <dt class="text-gray-500">Documentos fallidos</dt>
            <dd [class]="p.failedDocuments > 0 ? 'text-right text-red-700 dark:text-red-300' : 'text-right'">{{ fmt.number(p.failedDocuments) }}</dd>
          </dl>
        </div>
      } @empty {
        <p class="text-gray-500">No se ha ingerido ningún documento en este periodo.</p>
      }
    </div>
  `,
})
export class IngestionSectionComponent {
  private readonly chartTheme = inject(ChartTheme);

  readonly usage = input.required<IngestionUsage>();
  readonly previous = input<IngestionUsage | null>(null);

  protected readonly fmt = { number: formatNumber, ms: formatMs };

  protected readonly columns = computed(() => {
    const colors = this.chartTheme.colors();
    const previous = new Map((this.previous()?.pipelines ?? []).map(p => [p.pipeline.toLowerCase(), p.documents]));
    return this.usage().pipelines.map(p => ({
      ...p,
      label: pipelineLabel(p.pipeline),
      color: colors.pipeline(p.pipeline),
      change: percentChange(p.documents, previous.get(p.pipeline.toLowerCase())),
    }));
  });
}
