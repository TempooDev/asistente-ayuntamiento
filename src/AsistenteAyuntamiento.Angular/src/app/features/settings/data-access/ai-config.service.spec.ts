import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { provideTestDependencies } from '../../../../testing/test-providers';
import { AiConfigService } from './ai-config.service';

describe('AiConfigService', () => {
  let service: AiConfigService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: provideTestDependencies() });
    service = TestBed.inject(AiConfigService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the AI configuration from the settings endpoint', async () => {
    const promise = service.getConfiguration();
    const req = http.expectOne('/api/settings/ai');
    expect(req.request.method).toBe('GET');
    req.flush({ provider: 'google', model: 'gemini', temperature: 0.2, hasApiKey: true });

    await expect(promise).resolves.toMatchObject({ provider: 'google', hasApiKey: true });
  });

  it('saves the configuration with PUT', async () => {
    const dto = { provider: 'openai', model: 'gpt', temperature: 0.1 } as Parameters<AiConfigService['saveConfiguration']>[0];
    const promise = service.saveConfiguration(dto);
    const req = http.expectOne('/api/settings/ai');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(dto);
    req.flush(null);

    await promise;
  });
});
