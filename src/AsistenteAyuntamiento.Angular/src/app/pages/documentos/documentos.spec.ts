import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTestDependencies } from '../../../testing/test-providers';
import { DocumentosComponent } from './documentos';

describe('DocumentosComponent', () => {
  let fixture: ComponentFixture<DocumentosComponent>;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [DocumentosComponent],
      providers: provideTestDependencies(),
    }).compileComponents();

    fixture = TestBed.createComponent(DocumentosComponent);
  });

  it('should create', () => {
    expect(fixture.componentInstance).toBeTruthy();
  });
});
