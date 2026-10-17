import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { DocumentSource } from '../../../shared/models/document-source';
import { BlobInfo, IngestionStatus } from '../models/documents.models';

const STATUS_BADGES: Record<IngestionStatus, { label: string; classes: string }> = {
  [IngestionStatus.Completed]: { label: 'Procesado', classes: 'bg-green-100 text-green-800 dark:text-green-300' },
  [IngestionStatus.Processing]: { label: 'Procesando...', classes: 'bg-blue-100 text-blue-800 dark:text-blue-300 animate-pulse' },
  [IngestionStatus.Queued]: { label: 'Encolado', classes: 'bg-orange-100 text-orange-800 dark:text-orange-300 animate-pulse' },
  [IngestionStatus.Failed]: { label: 'Fallo', classes: 'bg-red-100 text-red-800 dark:text-red-300' },
  [IngestionStatus.Pending]: { label: 'Pendiente', classes: 'bg-yellow-100 text-yellow-800 dark:text-yellow-300' },
  [IngestionStatus.Unknown]: { label: 'Pendiente', classes: 'bg-yellow-100 text-yellow-800 dark:text-yellow-300' },
};

/** Blob path json/<SOURCE>/<id>.json → document id. */
export function documentIdOf(blobName: string): string {
  return blobName.split('/').at(-1)?.replace('.json', '') ?? '';
}

/** Blob path json/<SOURCE>/<id>.json → source code. */
export function sourceOf(blobName: string): string {
  return blobName.split('/')[1] ?? DocumentSource.Unknown;
}

/** Public URL of the original bulletin document, when its id format allows it. */
export function publicUrlOf(blobName: string): string | null {
  const id = documentIdOf(blobName);
  switch (sourceOf(blobName)) {
    case DocumentSource.BOE:
      return `https://www.boe.es/buscar/doc.php?id=${id}`;
    case DocumentSource.BOJA: {
      const [prefix, year, number, ...rest] = id.split('-');
      return prefix === DocumentSource.BOJA && rest.length > 0
        ? `https://www.juntadeandalucia.es/boja/${year}/${number}/${rest.join('-')}.html`
        : null;
    }
    default:
      return null;
  }
}

@Component({
  selector: 'app-document-table',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="table-container overflow-x-auto">
      <table class="table-auto w-full text-left border-collapse">
        <caption class="sr-only">Documentos almacenados y su estado de procesamiento</caption>
        <thead>
          <tr class="border-b border-outline">
            <th scope="col" class="p-sm font-label-lg w-8 text-center">
              <input type="checkbox" class="w-4 h-4 cursor-pointer" aria-label="Seleccionar todos los de esta página"
                     [checked]="allSelected()" (change)="toggleAll.emit(!allSelected())" />
            </th>
            <th scope="col" class="p-sm font-label-lg whitespace-nowrap">Nombre del archivo</th>
            <th scope="col" class="p-sm font-label-lg whitespace-nowrap">Tamaño</th>
            <th scope="col" class="p-sm font-label-lg whitespace-nowrap">Última modificación</th>
            <th scope="col" class="p-sm font-label-lg whitespace-nowrap">Estado</th>
            <th scope="col" class="p-sm font-label-lg whitespace-nowrap">Acciones</th>
          </tr>
        </thead>
        <tbody>
          @for (blob of blobs(); track blob.name) {
            <tr class="border-b border-surface-variant hover:bg-surface-variant/30 transition-colors">
              <td class="p-sm text-center">
                <input type="checkbox" class="w-4 h-4 cursor-pointer" [attr.aria-label]="'Seleccionar ' + blob.name"
                       [checked]="selected().has(blob.name)" (change)="toggle.emit(blob.name)" />
              </td>
              <td class="p-sm font-body-md font-medium text-primary">
                @if (urlOf(blob.name); as url) {
                  <a [href]="url" target="_blank" rel="noopener noreferrer" class="hover:underline text-blue-600 dark:text-blue-300 flex items-center gap-1">
                    {{ blob.name }}
                    <span class="material-symbols-outlined text-[14px]" aria-hidden="true">open_in_new</span>
                    <span class="sr-only">(se abre en una pestaña nueva)</span>
                  </a>
                } @else {
                  {{ blob.name }}
                }
              </td>
              <td class="p-sm font-body-md whitespace-nowrap">{{ kb(blob.size) }} KB</td>
              <td class="p-sm font-body-md whitespace-nowrap">{{ blob.lastModified ? (blob.lastModified | date: 'short') : '-' }}</td>
              <td class="p-sm font-body-md">
                <span class="inline-flex items-center px-2 py-0.5 rounded-sm text-xs font-medium" [class]="badge(blob.status).classes">{{ badge(blob.status).label }}</span>
              </td>
              <td class="p-sm font-body-md">
                <div class="flex gap-2">
                  <button type="button" class="btn btn-sm btn-outline border border-outline rounded-sm px-3 py-1"
                          [disabled]="blob.status === Status.Processing" (click)="process.emit(blob.name)">
                    {{ blob.status === Status.Completed ? 'Reprocesar' : blob.status === Status.Processing ? 'Procesando...' : 'Procesar' }}
                  </button>
                  @if (blob.status === Status.Processing) {
                    <button type="button" class="btn btn-sm btn-outline border border-red-700 text-red-700 dark:border-red-300 dark:text-red-300 rounded-sm px-3 py-1 hover:bg-red-700 hover:text-white dark:hover:bg-red-700 dark:hover:text-white transition-colors"
                            title="Reiniciar estado si se ha quedado atascado" (click)="reset.emit(blob.name)">
                      Reiniciar
                    </button>
                  }
                </div>
              </td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class DocumentTableComponent {
  readonly blobs = input.required<readonly BlobInfo[]>();
  readonly selected = input.required<ReadonlySet<string>>();
  readonly toggle = output<string>();
  readonly toggleAll = output<boolean>();
  readonly process = output<string>();
  readonly reset = output<string>();

  protected readonly Status = IngestionStatus;
  protected readonly allSelected = computed(() => this.blobs().length > 0 && this.blobs().every(b => this.selected().has(b.name)));

  protected urlOf = publicUrlOf;
  protected kb = (bytes: number) => Math.round(bytes / 1024);
  protected badge = (status: IngestionStatus) => STATUS_BADGES[status] ?? STATUS_BADGES[IngestionStatus.Pending];
}
