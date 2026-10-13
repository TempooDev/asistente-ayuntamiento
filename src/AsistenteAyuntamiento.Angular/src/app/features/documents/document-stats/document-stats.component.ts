import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { BlobStats, BlobStatusFilter } from '../models/documents.models';

/** Counters by ingestion state; clicking one filters the list. */
@Component({
  selector: 'app-document-stats',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-wrap gap-4 mb-md">
      @for (card of cards; track card.label) {
        @if (card.filter) {
          <button
            type="button"
            class="p-4 bg-surface rounded border border-outline flex-1 min-w-[150px] shadow-sm text-left hover:bg-surface-variant transition-colors"
            [attr.aria-pressed]="selected() === card.filter"
            (click)="filterSelected.emit(card.filter)">
            <div class="text-sm opacity-70">{{ card.label }}</div>
            <div class="text-2xl font-bold" [class]="card.color">{{ stats()[card.key] }}</div>
          </button>
        } @else {
          <div class="p-4 bg-surface rounded border border-outline flex-1 min-w-[150px] shadow-sm">
            <div class="text-sm opacity-70">{{ card.label }}</div>
            <div class="text-2xl font-bold" [class]="card.color">{{ stats()[card.key] }}</div>
          </div>
        }
      }
    </div>
  `,
})
export class DocumentStatsComponent {
  readonly stats = input.required<BlobStats>();
  readonly selected = input<BlobStatusFilter>();
  readonly filterSelected = output<BlobStatusFilter>();

  protected readonly cards: readonly { label: string; key: keyof BlobStats; filter?: BlobStatusFilter; color: string }[] = [
    { label: 'Pendientes', key: 'pending', filter: 'Pendientes', color: '' },
    { label: 'Encolados', key: 'queued', filter: 'Encolados', color: 'text-orange-500' },
    { label: 'En proceso', key: 'processing', color: 'text-blue-500' },
    { label: 'Completados', key: 'completed', filter: 'Procesados', color: 'text-green-500' },
    { label: 'Total Docs', key: 'total', filter: 'Todos', color: '' },
  ];
}
