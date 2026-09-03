import { TestBed } from '@angular/core/testing';

import { AiConfig } from './ai-config';

describe('AiConfig', () => {
  let service: AiConfig;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(AiConfig);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
