import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of } from 'rxjs';
import { provideTestDependencies } from '../../../../testing/test-providers';
import { ConfirmService } from '../../../shared/ui/confirm.service';
import { IngestionService } from '../data-access/ingestion.service';
import { BlobQuery, IngestionStatus, PaginatedBlobsResponse } from '../models/documents.models';
import { DocumentsPageComponent } from './documents-page.component';

const page = (names: string[]): PaginatedBlobsResponse => ({
  items: names.map(name => ({ name, size: 2048, isProcessed: false, status: IngestionStatus.Pending })),
  totalCount: names.length,
  stats: { total: names.length, pending: names.length, queued: 0, processing: 0, completed: 0 },
});

class FakeIngestion {
  queries: BlobQuery[] = [];
  reprocessCalls = 0;
  getBlobs(query: BlobQuery): Observable<PaginatedBlobsResponse> {
    this.queries.push(query);
    return of(page([`json/BOE/BOE-A-${this.queries.length}.json`]));
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

  beforeEach(async () => {
    vi.useFakeTimers();
    localStorage.clear();
    ingestion = new FakeIngestion();
    await TestBed.configureTestingModule({
      imports: [DocumentsPageComponent],
      providers: [provideTestDependencies(), { provide: IngestionService, useValue: ingestion }],
    }).compileComponents();
    fixture = TestBed.createComponent(DocumentsPageComponent);
    component = fixture.componentInstance as DocumentsPageComponent & Record<string, unknown>;
    fixture.detectChanges();
    await vi.runAllTimersAsync();
  });

  afterEach(() => vi.useRealTimers());

  const changeFilters = (change: object) => (component['changeFilters'] as (c: object) => void).call(component, change);

  it('loads the first page with the default filters', () => {
    expect(ingestion.queries).toHaveLength(1);
    expect(ingestion.queries[0]).toEqual(expect.objectContaining({ page: 1, pageSize: 20, status: 'Pendientes' }));
    expect(fixture.nativeElement.textContent).toContain('BOE-A-1');
  });

  it('waits for a pause in typing before searching, and searches once', async () => {
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
    changeFilters({ status: 'Todos' });
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);

    expect(ingestion.queries.at(-1)).toEqual(expect.objectContaining({ status: 'Todos', page: 1 }));
    expect(JSON.parse(localStorage.getItem('docFilters')!).status).toBe('Todos');
  });

  it('asks for confirmation before reprocessing everything', async () => {
    const confirm = TestBed.inject(ConfirmService);
    const answer = (component['reprocessAll'] as () => Promise<void>).call(component);
    expect(confirm.pending()?.title).toContain('Reprocesar');

    confirm.answer(false);
    await answer;
    expect(ingestion.reprocessCalls).toBe(0);
  });
});
