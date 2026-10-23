import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { HttpTestingController } from '@angular/common/http/testing';
import { provideTestDependencies } from '../../../../testing/test-providers';
import { IngestionService } from './ingestion.service';

describe('IngestionService', () => {
  let service: IngestionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: provideTestDependencies() });
    service = TestBed.inject(IngestionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('omits empty filters from the blob query', async () => {
    const promise = firstValueFrom(service.getBlobs({ page: 2, pageSize: 20, status: 'Todos', search: '', dateFrom: null }));
    const req = http.expectOne(r => r.url === '/api/ingestion/blobs');
    expect(req.request.params.keys()).toEqual(['page', 'pageSize', 'status']);
    expect(req.request.params.get('page')).toBe('2');
    req.flush({ items: [], total: 0, page: 2, pageSize: 20, stats: { total: 0, pending: 0, processing: 0, completed: 0 } });

    await expect(promise).resolves.toMatchObject({ total: 0 });
  });

  it('sends the pipeline the status filter applies to', () => {
    service.getBlobs({ page: 1, pageSize: 20, status: 'Encolados', pipeline: 'HIERARCHICAL' }).subscribe();
    const req = http.expectOne(r => r.url === '/api/ingestion/blobs');
    expect(req.request.params.get('pipeline')).toBe('HIERARCHICAL');
    req.flush({ items: [], total: 0, page: 1, pageSize: 20, stats: { total: 0, pending: 0, queued: 0, processing: 0, completed: 0 } });
  });

  it('sends the pipeline mode when enqueuing in bulk', async () => {
    const promise = firstValueFrom(service.enqueueBulk([{ blobPath: 'boe/a.pdf', source: 'BOE' }], 'HIERARCHICAL'));
    const req = http.expectOne(r => r.url === '/api/ingestion/enqueue-bulk');
    expect(req.request.params.get('pipelineMode')).toBe('HIERARCHICAL');
    req.flush({ message: 'ok' });

    await expect(promise).resolves.toBe('ok');
  });
});
