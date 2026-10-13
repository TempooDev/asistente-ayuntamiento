import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTestDependencies } from '../../../testing/test-providers';
import { ConfiguracionComponent } from './configuracion';

describe('ConfiguracionComponent', () => {
  let fixture: ComponentFixture<ConfiguracionComponent>;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [ConfiguracionComponent],
      providers: provideTestDependencies(),
    }).compileComponents();

    fixture = TestBed.createComponent(ConfiguracionComponent);
  });

  it('should create', () => {
    expect(fixture.componentInstance).toBeTruthy();
  });
});
