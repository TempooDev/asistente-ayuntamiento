import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { EMPTY, Observable, catchError, debounce, distinctUntilChanged, exhaustMap, finalize, interval, of, switchMap, tap, timer } from 'rxjs';
import { DocumentStatusNotification, NotificationService } from '../../../core/realtime/notification.service';
import { ConfirmService } from '../../../shared/ui/confirm.service';
import { ToastService } from '../../../shared/ui/toast.service';
import { IngestionService } from '../data-access/ingestion.service';
import { DocumentFilters, DocumentFiltersComponent } from '../document-filters/document-filters.component';
import { DocumentStatsComponent } from '../document-stats/document-stats.component';
import { DocumentTableComponent, documentIdOf, sourceOf } from '../document-table/document-table.component';
import { PipelineProgressComponent } from '../pipeline-progress/pipeline-progress.component';
import {
  BlobInfo,
  BlobQuery,
  BlobStats,
  BlobStatusFilter,
  DOCUMENT_PIPELINES,
  IngestionStatus,
  PaginatedBlobsResponse,
  PipelineMode,
  PipelineStats,
} from '../models/documents.models';

const FILTERS_KEY = 'docFilters';
const SEARCH_DEBOUNCE_MS = 300;
/** Refresh interval of the counters and the visible page while some pipeline is still working. */
export const AUTO_REFRESH_MS = 10_000;
const DEFAULT_FILTERS: DocumentFilters = { pageSize: 20, status: 'Todos', pipeline: null, search: '', dateFrom: null, dateTo: null, minSizeKb: null, maxSizeKb: null };

/** Admin page of the stored documents: filters, live ingestion state and (re)processing actions. */
@Component({
  selector: 'app-documents-page',
  imports: [DocumentStatsComponent, PipelineProgressComponent, DocumentFiltersComponent, DocumentTableComponent],
  templateUrl: './documents-page.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DocumentsPageComponent {
  private readonly ingestion = inject(IngestionService);
  private readonly notifications = inject(NotificationService);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly filters = signal<DocumentFilters>(loadFilters());
  protected readonly page = signal(1);
  private readonly reload = signal(0);

  protected readonly blobs = signal<readonly BlobInfo[]>([]);
  protected readonly stats = signal<BlobStats | null>(null);
  protected readonly pipelineStats = signal<readonly PipelineStats[]>([]);
  protected readonly filteredCount = signal(0);
  protected readonly isLoading = signal(false);
  protected readonly statusMessage = signal('');
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly pipelineMode = signal<PipelineMode>('BOTH');

  /** Where "Encolar" and "Reprocesar" publish; the study variants are never sent unless chosen. */
  protected readonly pipelineModes: readonly { value: PipelineMode; label: string }[] = [
    { value: 'BOTH', label: 'Baseline y jerárquica' },
    { value: 'BASELINE', label: 'Solo baseline (chunks fijos)' },
    { value: 'HIERARCHICAL', label: 'Solo jerárquica' },
    { value: 'RECURSIVE', label: 'Solo recursiva (estudio)' },
    { value: 'SEMANTIC', label: 'Solo semántica (estudio)' },
    { value: 'RECURSIVE,SEMANTIC', label: 'Recursiva y semántica (estudio)' },
    { value: 'ALL', label: 'Las cuatro variantes' },
  ];

  /** The chunking study variants are shown once some document was sent to them. */
  protected readonly studyActive = computed(() =>
    this.pipelineStats().some(p => isStudyPipeline(p.pipeline) && p.notSent < p.total),
  );
  protected readonly visiblePipelineStats = computed(() =>
    this.pipelineStats().filter(p => !isStudyPipeline(p.pipeline) || this.studyActive()),
  );

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.filteredCount() / this.filters().pageSize)));
  private readonly query = computed<BlobQuery & { reload: number }>(() => ({ ...this.filters(), page: this.page(), reload: this.reload() }));

  /** Something is queued or processing in some pipeline (the summary counters when the API sends no per-pipeline ones). */
  protected readonly ingesting = computed(() => {
    const pipelines = this.pipelineStats();
    if (pipelines.length > 0) return pipelines.some(p => p.queued + p.processing > 0);
    const s = this.stats();
    return !!s && s.queued + s.processing > 0;
  });

  constructor() {
    effect(() => saveFilters(this.filters()));

    // Typing in the search box waits for a pause; any other change reloads at once. switchMap drops stale responses.
    let previousSearch = this.filters().search;
    toObservable(this.query)
      .pipe(
        debounce(query => {
          const searchChanged = query.search !== previousSearch;
          previousSearch = query.search;
          return searchChanged ? timer(SEARCH_DEBOUNCE_MS) : of(0);
        }),
        distinctUntilChanged((a, b) => JSON.stringify(a) === JSON.stringify(b)),
        tap(() => this.isLoading.set(true)),
        switchMap(({ reload: _, ...query }) =>
          this.ingestion.getBlobs(query).pipe(
            catchError(err => {
              this.statusMessage.set(`Error al cargar: ${errorText(err)}`);
              return of(null);
            }),
            finalize(() => this.isLoading.set(false)),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(result => {
        if (result) this.applyPage(result);
      });

    // Silent refresh (no spinner) of the counters and the visible page; stops when nothing is left to ingest.
    toObservable(this.ingesting)
      .pipe(
        distinctUntilChanged(),
        switchMap(active => (active ? interval(AUTO_REFRESH_MS) : EMPTY)),
        exhaustMap(() => {
          const { reload: _, ...query } = this.query();
          return this.ingestion.getBlobs(query).pipe(catchError(() => of(null)));
        }),
        takeUntilDestroyed(),
      )
      .subscribe(result => {
        if (result) this.applyPage(result);
      });

    void this.notifications.connect();
    this.notifications.documentStatusUpdates$
      .pipe(takeUntilDestroyed())
      .subscribe(changes => this.applyStatuses(changes));
  }

  protected changeFilters(change: Partial<DocumentFilters>): void {
    this.filters.update(current => ({ ...current, ...change }));
    this.page.set(1);
  }

  protected showStatus(status: BlobStatusFilter): void {
    this.changeFilters({ status });
  }

  protected changePage(page: number): void {
    if (page >= 1 && page <= this.totalPages()) this.page.set(page);
  }

  protected toggle(name: string): void {
    this.selected.update(current => {
      const next = new Set(current);
      if (!next.delete(name)) next.add(name);
      return next;
    });
  }

  protected toggleAll(select: boolean): void {
    this.selected.update(current => {
      const next = new Set(current);
      for (const blob of this.blobs()) {
        if (select) next.add(blob.name);
        else next.delete(blob.name);
      }
      return next;
    });
  }

  protected process(name: string): void {
    this.statusMessage.set(`Procesando ${name}... (esto puede tardar unos minutos)`);
    this.setStatus(name, IngestionStatus.Processing);
    this.act(this.ingestion.processBlob(name, sourceOf(name)), () => this.setStatus(name, IngestionStatus.Completed), () =>
      this.setStatus(name, IngestionStatus.Failed),
    );
  }

  protected reset(name: string): void {
    this.statusMessage.set(`Reiniciando estado de ${name}...`);
    this.act(this.ingestion.resetBlobStatus(documentIdOf(name)), () => this.setStatus(name, IngestionStatus.Pending));
  }

  protected enqueueSelected(): void {
    const names = [...this.selected()];
    if (names.length === 0) return;
    this.statusMessage.set(`Encolando ${names.length} documentos...`);
    this.act(this.ingestion.enqueueBulk(names.map(name => ({ blobPath: name, source: sourceOf(name) })), this.pipelineMode()), () => {
      this.blobs.update(list => list.map(b => (this.selected().has(b.name) ? { ...b, status: IngestionStatus.Queued } : b)));
      this.stats.update(s => (s ? { ...s, pending: Math.max(0, s.pending - names.length), queued: s.queued + names.length } : s));
      this.selected.set(new Set());
    });
  }

  protected resetStuck(): void {
    this.statusMessage.set('Reiniciando todos los documentos colgados...');
    this.act(this.ingestion.resetStuckProcessing(), () => this.reload.update(n => n + 1));
  }

  protected async reprocessAll(): Promise<void> {
    const confirmed = await this.confirm.ask({
      title: 'Reprocesar todo el histórico',
      message: 'Se encolarán todos los documentos almacenados para procesarlos de nuevo. Puede tardar horas y consume cuota de IA.',
      confirmLabel: 'Reprocesar',
      danger: true,
    });
    if (!confirmed) return;
    this.statusMessage.set('Iniciando reprocesado masivo...');
    this.act(this.ingestion.reprocessAll(this.pipelineMode()), () => this.reload.update(n => n + 1));
  }

  protected setPipelineMode(value: string): void {
    this.pipelineMode.set(value as PipelineMode);
  }

  private applyPage(result: PaginatedBlobsResponse): void {
    this.blobs.set(result.items);
    this.stats.set(result.stats);
    this.pipelineStats.set(result.pipelineStats ?? []);
    this.filteredCount.set(result.total);
  }

  /** Runs an action, shows the server message (or the error) and applies the local update on success. */
  private act(request: Observable<string>, onSuccess: () => void, onError?: () => void): void {
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: message => {
        this.statusMessage.set(message);
        onSuccess();
      },
      error: err => {
        this.statusMessage.set('');
        this.toast.error(`Error: ${errorText(err)}`);
        onError?.();
      },
    });
  }

  private setStatus(name: string, status: IngestionStatus): void {
    this.blobs.update(list => list.map(b => (b.name === name ? { ...b, status, isProcessed: status === IngestionStatus.Completed } : b)));
  }

  /** Applies a batch of status changes in one update of the list and the counters. */
  private applyStatuses(changes: readonly DocumentStatusNotification[]): void {
    if (changes.length === 0) return;
    const statusById = new Map(changes.map(c => [c.documentId, c.status as IngestionStatus]));

    this.blobs.update(list =>
      list.map(b => {
        const status = statusById.get(documentIdOf(b.name));
        return status ? { ...b, status, isProcessed: status === IngestionStatus.Completed } : b;
      }),
    );
    this.stats.update(s => {
      if (!s) return s;
      let { queued, processing, completed } = s;
      for (const status of statusById.values()) {
        if (status === IngestionStatus.Processing && queued > 0) {
          queued--;
          processing++;
        } else if (status === IngestionStatus.Completed && processing > 0) {
          processing--;
          completed++;
        }
      }
      return { ...s, queued, processing, completed };
    });
  }
}

function isStudyPipeline(pipeline: string): boolean {
  return DOCUMENT_PIPELINES.some(p => p.study && p.key === pipeline.toLowerCase());
}

function errorText(err: unknown): string {
  const e = err as { error?: { message?: string; detail?: string }; message?: string };
  return e?.error?.message ?? e?.error?.detail ?? e?.message ?? String(err);
}

function loadFilters(): DocumentFilters {
  try {
    const saved = localStorage.getItem(FILTERS_KEY);
    return saved ? { ...DEFAULT_FILTERS, ...(JSON.parse(saved) as Partial<DocumentFilters>) } : DEFAULT_FILTERS;
  } catch {
    return DEFAULT_FILTERS;
  }
}

function saveFilters(filters: DocumentFilters): void {
  try {
    localStorage.setItem(FILTERS_KEY, JSON.stringify(filters));
  } catch {
    // Not persisted.
  }
}

