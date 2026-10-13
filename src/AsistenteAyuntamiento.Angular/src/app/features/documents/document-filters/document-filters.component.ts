import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { BlobQuery, BlobStatusFilter } from '../models/documents.models';

export type DocumentFilters = Omit<BlobQuery, 'page'>;

/** Filters of the document list. Emits partial changes; the page decides when to reload. */
@Component({
  selector: 'app-document-filters',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-md flex flex-col gap-4" role="search" aria-label="Filtrar documentos">
      <div class="flex gap-4 items-center flex-wrap">
        <label class="sr-only" for="doc-search">Buscar por nombre</label>
        <input id="doc-search" #search type="search" class="px-3 py-2 border rounded-sm bg-transparent w-full max-w-xs" placeholder="Buscar por nombre..."
               [value]="filters().search ?? ''" (input)="changed.emit({ search: search.value })" />

        <label class="sr-only" for="doc-status">Estado</label>
        <select id="doc-status" #status class="px-3 py-2 border rounded-sm bg-transparent" (change)="changed.emit({ status: asStatus(status.value) })">
          @for (option of statuses; track option.value) {
            <option [value]="option.value" [selected]="filters().status === option.value">{{ option.label }}</option>
          }
        </select>

        <label class="flex items-center gap-2 text-sm font-medium">Desde:
          <input #from type="date" class="px-2 py-1 border rounded-sm bg-transparent font-normal" [value]="filters().dateFrom ?? ''" (change)="changed.emit({ dateFrom: from.value || null })" />
        </label>
        <label class="flex items-center gap-2 text-sm font-medium">Hasta:
          <input #to type="date" class="px-2 py-1 border rounded-sm bg-transparent font-normal" [value]="filters().dateTo ?? ''" (change)="changed.emit({ dateTo: to.value || null })" />
        </label>
      </div>

      <div class="flex gap-4 items-center flex-wrap">
        <label class="flex items-center gap-2 text-sm font-medium">Tamaño Min (KB):
          <input #min type="number" min="0" class="px-2 py-1 border rounded-sm bg-transparent w-24 font-normal" placeholder="Min" [value]="filters().minSizeKb ?? ''" (change)="changed.emit({ minSizeKb: toNumber(min.value) })" />
        </label>
        <label class="flex items-center gap-2 text-sm font-medium">Tamaño Max (KB):
          <input #max type="number" min="0" class="px-2 py-1 border rounded-sm bg-transparent w-24 font-normal" placeholder="Max" [value]="filters().maxSizeKb ?? ''" (change)="changed.emit({ maxSizeKb: toNumber(max.value) })" />
        </label>

        <label class="flex items-center gap-2 ml-auto text-sm font-medium">Por página:
          <select #size class="px-2 py-1 border rounded-sm bg-transparent font-normal" (change)="changed.emit({ pageSize: +size.value })">
            @for (n of pageSizes; track n) {
              <option [value]="n" [selected]="filters().pageSize === n">{{ n }}</option>
            }
          </select>
        </label>

        <div class="text-sm opacity-70" aria-live="polite">Mostrando {{ shown() }} de {{ total() }}</div>
      </div>
    </div>
  `,
})
export class DocumentFiltersComponent {
  readonly filters = input.required<DocumentFilters>();
  readonly shown = input(0);
  readonly total = input(0);
  readonly changed = output<Partial<DocumentFilters>>();

  protected readonly statuses: readonly { value: BlobStatusFilter; label: string }[] = [
    { value: 'Todos', label: 'Todos los estados' },
    { value: 'Procesados', label: 'Solo Procesados' },
    { value: 'Encolados', label: 'Solo Encolados' },
    { value: 'Pendientes', label: 'Solo Pendientes' },
  ];
  protected readonly pageSizes = [10, 20, 50, 100];

  protected asStatus(value: string): BlobStatusFilter {
    return value as BlobStatusFilter;
  }

  protected toNumber(value: string): number | null {
    return value === '' ? null : Number(value);
  }
}
