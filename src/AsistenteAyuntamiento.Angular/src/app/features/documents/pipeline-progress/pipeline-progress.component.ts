import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DOCUMENT_PIPELINES, PipelineStats } from '../models/documents.models';

type SegmentKey = 'completed' | 'processing' | 'queued' | 'failed' | 'pending' | 'notSent';

const SEGMENTS: readonly { key: SegmentKey; label: string; bar: string; dot: string }[] = [
  { key: 'completed', label: 'Completados', bar: 'bg-green-600', dot: 'bg-green-600' },
  { key: 'processing', label: 'En proceso', bar: 'bg-blue-500', dot: 'bg-blue-500' },
  { key: 'queued', label: 'Encolados', bar: 'bg-orange-500', dot: 'bg-orange-500' },
  { key: 'failed', label: 'Fallidos', bar: 'bg-red-600', dot: 'bg-red-600' },
  { key: 'pending', label: 'Pendientes', bar: 'bg-yellow-300', dot: 'bg-yellow-300' },
  { key: 'notSent', label: 'No enviados', bar: 'bg-gray-300', dot: 'bg-gray-300' },
];

const LABELS: Record<string, string> = Object.fromEntries(DOCUMENT_PIPELINES.map(p => [p.key, p.label]));

/** One stacked progress bar per pipeline: completed, processing, queued, failed, pending and not sent out of the total. */
@Component({
  selector: 'app-pipeline-progress',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="grid grid-cols-1 lg:grid-cols-2 gap-4 mb-md">
      @for (p of stats(); track p.pipeline) {
        <div class="p-4 bg-surface rounded-sm border border-outline shadow-xs" data-testid="pipeline-progress">
          <div class="flex justify-between items-baseline mb-2">
            <h2 class="font-bold">{{ label(p.pipeline) }}</h2>
            <span class="text-sm opacity-80">{{ p.completed }} de {{ p.total }} completados</span>
          </div>
          <div class="flex h-3 w-full overflow-hidden rounded-full bg-surface-variant" role="img"
               [attr.aria-label]="label(p.pipeline) + ': ' + summary(p)">
            @for (s of segments; track s.key) {
              @if (p[s.key] > 0) {
                <div class="h-full" [class]="s.bar" [style.width.%]="percent(p, s.key)" [title]="s.label + ': ' + p[s.key]"></div>
              }
            }
          </div>
          <ul class="flex flex-wrap gap-x-4 gap-y-1 mt-2 text-xs">
            @for (s of segments; track s.key) {
              <li class="flex items-center gap-1">
                <span class="inline-block w-2 h-2 rounded-full" [class]="s.dot" aria-hidden="true"></span>
                {{ s.label }}: <span class="font-medium">{{ p[s.key] }}</span>
              </li>
            }
          </ul>
        </div>
      }
    </div>
  `,
})
export class PipelineProgressComponent {
  readonly stats = input.required<readonly PipelineStats[]>();

  protected readonly segments = SEGMENTS;

  protected label(pipeline: string): string {
    return LABELS[pipeline.toLowerCase()] ?? pipeline;
  }

  protected percent(p: PipelineStats, key: SegmentKey): number {
    return p.total > 0 ? (p[key] / p.total) * 100 : 0;
  }

  protected summary(p: PipelineStats): string {
    return SEGMENTS.map(s => `${p[s.key]} ${s.label.toLowerCase()}`).join(', ') + ` de ${p.total}`;
  }
}
