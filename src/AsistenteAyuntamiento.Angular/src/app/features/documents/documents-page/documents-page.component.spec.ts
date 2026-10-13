import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTestDependencies } from '../../../../testing/test-providers';
import { DocumentsPageComponent } from './documents-page.component';

describe('DocumentsPageComponent', () => {
  let fixture: ComponentFixture<DocumentsPageComponent>;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [DocumentsPageComponent],
      providers: provideTestDependencies(),
    }).compileComponents();

    fixture = TestBed.createComponent(DocumentsPageComponent);
  });

  it('should create', () => {
    expect(fixture.componentInstance).toBeTruthy();
  });
});
