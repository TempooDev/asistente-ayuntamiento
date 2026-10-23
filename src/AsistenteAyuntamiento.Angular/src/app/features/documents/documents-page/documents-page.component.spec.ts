import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of } from 'rxjs';
import { provideTestDependencies } from '../../../../testing/test-providers';
import { ConfirmService } from '../../../shared/ui/confirm.service';
import { IngestionService } from '../data-access/ingestion.service';
import { BlobQuery, IngestionStatus, PaginatedBlobsResponse, PipelineStats } from '../models/documents.models';
import { AUTO_REFRESH_MS, DocumentsPageComponent } from './documents-page.component';

const idle: PipelineStats[] = [
  { pipeline: 'Baseline', total: 1, completed: 1, processing: 0, queued: 0, failed: 0, pending: 0, notSent: 0 },
  { pipeline: 'Hierarchical', total: 1, completed: 1, processing: 0, queued: 0, failed: 0, pending: 0, notSent: 0 },
];
const busy: PipelineStats[] = [
  { pipeline: 'Baseline', total: 201, completed: 150, processing: 5, queued: 46, failed: 0, pending: 0, notSent: 0 },
  { pipeline: 'Hierarchical', total: 201, completed: 3, processing: 5, queued: 193, failed: 0, pending: 0, notSent: 0 },
];

const page = (names: string[], pipelineStats: PipelineStats[] = idle): PaginatedBlobsResponse => ({
  items: names.map(name => ({
    name,
    size: 2048,
    isProcessed: false,
    status: IngestionStatus.Queued,
    baselineStatus: IngestionStatus.Failed,
    hierarchicalStatus: null,
    errorMessage: 'Timeout del embedding',
  })),
  total: names.length,
  page: 1,
  pageSize: 20,
  stats: { total: names.length, pending: names.length, queued: 0, processing: 0, completed: 0 },
  pipelineStats,
});

class FakeIngestion {
  queries: BlobQuery[] = [];
  reprocessCalls = 0;
  /** Per-pipeline counters returned by the next requests. */
  pipelineStats: PipelineStats[] = idle;
  getBlobs(query: BlobQuery): Observable<PaginatedBlobsResponse> {
    this.queries.push(query);
    return of(page([`json/BOE/BOE-A-${this.queries.length}.json`], this.pipelineStats));
  }
  reprocessAll(): Observable<string> {
    this.reprocessCalls++;
    return of('ok');
  }
}

describe('DocumentsPageComponent', () => {
  let fixture: ComponentFixture<DocumentsPageComponent>;
  let ingestion: FakeIngestion;
  let component: DocumentsPageComponent & Record<string, unknown>;

  const create = async (pipelineStats: PipelineStats[] = idle) => {
    ingestion.pipelineStats = pipelineStats;
    fixture = TestBed.createComponent(DocumentsPageComponent);
    component = fixture.componentInstance as DocumentsPageComponent & Record<string, unknown>;
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
  };

  beforeEach(async () => {
    vi.useFakeTimers();
    localStorage.clear();
    ingestion = new FakeIngestion();
    await TestBed.configureTestingModule({
      imports: [DocumentsPageComponent],
      providers: [provideTestDependencies(), { provide: IngestionService, useValue: ingestion }],
    }).compileComponents();
  });

  afterEach(() => vi.useRealTimers());

  const changeFilters = (change: object) => (component['changeFilters'] as (c: object) => void).call(component, change);

  it('loads the first page with "Todos" as the default status filter', async () => {
    await create();
    expect(ingestion.queries).toHaveLength(1);
    expect(ingestion.queries[0]).toEqual(expect.objectContaining({ page: 1, pageSize: 20, status: 'Todos' }));
    expect(fixture.nativeElement.textContent).toContain('BOE-A-1');
  });

  it('waits for a pause in typing before searching, and searches once', async () => {
    await create();
    changeFilters({ search: 'B' });
    fixture.detectChanges();
    changeFilters({ search: 'BO' });
    fixture.detectChanges();
    changeFilters({ search: 'BOE' });
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(100);
    expect(ingestion.queries).toHaveLength(1);

    await vi.advanceTimersByTimeAsync(300);
    expect(ingestion.queries).toHaveLength(2);
    expect(ingestion.queries[1].search).toBe('BOE');
  });

  it('reloads at once for other filters and returns to page 1', async () => {
    await create();
    changeFilters({ status: 'Encolados', pipeline: 'HIERARCHICAL' });
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);

    expect(ingestion.queries.at(-1)).toEqual(expect.objectContaining({ status: 'Encolados', pipeline: 'HIERARCHICAL', page: 1 }));
    expect(JSON.parse(localStorage.getItem('docFilters')!).status).toBe('Encolados');
  });

  it('shows one progress bar per pipeline and one status column per pipeline with the error as tooltip', async () => {
    await create(busy);
    const element = fixture.nativeElement as HTMLElement;

    const bars = element.querySelectorAll('[data-testid="pipeline-progress"]');
    expect(bars).toHaveLength(2);
    expect(bars[0].textContent).toContain('Baseline');
    expect(bars[0].textContent).toContain('150 de 201 completados');
    expect(bars[1].textContent).toContain('Jerárquica');
    expect(bars[1].textContent).toContain('3 de 201 completados');

    const headers = [...element.querySelectorAll('th')].map(th => th.textContent!.trim());
    expect(headers).toEqual(expect.arrayContaining(['Baseline', 'Jerárquica']));
    const baseline = element.querySelector('[data-testid="baseline-status"] span')!;
    expect(baseline.textContent).toContain('Fallo');
    expect(baseline.getAttribute('title')).toBe('Timeout del embedding');
    const hierarchical = element.querySelector('[data-testid="hierarchical-status"] span')!;
    expect(hierarchical.textContent).toContain('No enviado');
    expect(hierarchical.getAttribute('title')).toBeNull();
  });

  it('refreshes every 10 seconds while something is queued or processing, and stops when nothing is', async () => {
    await create(busy);
    expect(ingestion.queries).toHaveLength(1);

    await vi.advanceTimersByTimeAsync(AUTO_REFRESH_MS);
    expect(ingestion.queries).toHaveLength(2);

    ingestion.pipelineStats = idle;
    await vi.advanceTimersByTimeAsync(AUTO_REFRESH_MS);
    fixture.detectChanges();
    expect(ingestion.queries).toHaveLength(3);

    await vi.advanceTimersByTimeAsync(AUTO_REFRESH_MS * 3);
    expect(ingestion.queries).toHaveLength(3);
  });

  it('does not refresh when nothing is being ingested', async () => {
    await create(idle);
    await vi.advanceTimersByTimeAsync(AUTO_REFRESH_MS * 3);
    expect(ingestion.queries).toHaveLength(1);
  });

  it('stops refreshing when the page is destroyed', async () => {
    await create(busy);
    fixture.destroy();
    await vi.advanceTimersByTimeAsync(AUTO_REFRESH_MS * 3);
    expect(ingestion.queries).toHaveLength(1);
  });

  it('asks for confirmation before reprocessing everything', async () => {
    await create();
    const confirm = TestBed.inject(ConfirmService);
    const answer = (component['reprocessAll'] as () => Promise<void>).call(component);
    expect(confirm.pending()?.title).toContain('Reprocesar');

    confirm.answer(false);
    await answer;
    expect(ingestion.reprocessCalls).toBe(0);
  });
});
