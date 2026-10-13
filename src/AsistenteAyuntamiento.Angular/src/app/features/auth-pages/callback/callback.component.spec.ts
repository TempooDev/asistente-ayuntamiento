import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTestDependencies } from '../../../../testing/test-providers';
import { CallbackComponent } from './callback.component';

describe('CallbackComponent', () => {
  let fixture: ComponentFixture<CallbackComponent>;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [CallbackComponent],
      providers: provideTestDependencies(),
    }).compileComponents();

    fixture = TestBed.createComponent(CallbackComponent);
  });

  it('should create', () => {
    expect(fixture.componentInstance).toBeTruthy();
  });
});
