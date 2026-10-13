import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { provideTestDependencies } from '../../../testing/test-providers';
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
    const promise = service.getBlobs({ page: 2, search: '', status: null, source: undefined });
    const req = http.expectOne(r => r.url === '/api/ingestion/blobs');
    expect(req.request.params.keys()).toEqual(['page']);
    expect(req.request.params.get('page')).toBe('2');
    req.flush({ items: [], totalCount: 0, stats: { total: 0, pending: 0, processing: 0, completed: 0 } });

    await expect(promise).resolves.toMatchObject({ totalCount: 0 });
  });

  it('sends the pipeline mode when enqueuing in bulk', async () => {
    const promise = service.enqueueBulk([{ blobPath: 'boe/a.pdf', source: 'BOE' }], 'HIERARCHICAL');
    const req = http.expectOne(r => r.url === '/api/ingestion/enqueue-bulk');
    expect(req.request.params.get('pipelineMode')).toBe('HIERARCHICAL');
    req.flush({ message: 'ok' });

    await expect(promise).resolves.toBe('ok');
  });
});
