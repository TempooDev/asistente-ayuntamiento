import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTestDependencies } from '../../../testing/test-providers';
import { PerfilComponent } from './perfil';

describe('PerfilComponent', () => {
  let fixture: ComponentFixture<PerfilComponent>;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [PerfilComponent],
      providers: provideTestDependencies(),
    }).compileComponents();

    fixture = TestBed.createComponent(PerfilComponent);
  });

  it('should create', () => {
    expect(fixture.componentInstance).toBeTruthy();
  });
});
