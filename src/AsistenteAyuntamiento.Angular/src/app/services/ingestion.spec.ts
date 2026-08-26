import { TestBed } from '@angular/core/testing';

import { Ingestion } from './ingestion';

describe('Ingestion', () => {
  let service: Ingestion;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(Ingestion);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
