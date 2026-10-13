import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, throwError } from 'rxjs';
import { provideTestDependencies } from '../../../../testing/test-providers';
import { ArenaService } from '../data-access/arena.service';
import { ArenaCompareResponse } from '../models/arena.models';
import { ArenaPageComponent } from './arena-page.component';

describe('ArenaPageComponent', () => {
  const render = async (error: HttpErrorResponse) => {
    const arena = {
      currentQuery: signal('¿Qué plazo hay?'),
      compareData: signal<ArenaCompareResponse | null>(null),
      voteResult: signal(null),
      compare: (): Observable<ArenaCompareResponse> => throwError(() => error),
    };
    await TestBed.configureTestingModule({
      imports: [ArenaPageComponent],
      providers: [provideTestDependencies(), { provide: ArenaService, useValue: arena }],
    }).compileComponents();
    const fixture = TestBed.createComponent(ArenaPageComponent);
    await fixture.componentInstance.onCompare();
    fixture.detectChanges();
    return { page: fixture.nativeElement as HTMLElement, arena };
  };

  it('shows why a side failed and offers nothing to vote on', async () => {
    const { page, arena } = await render(new HttpErrorResponse({
      status: 502,
      error: { title: 'LLM_QUOTA_EXCEEDED', detail: 'Hemos alcanzado el límite de uso del modelo de IA. Inténtalo de nuevo más tarde.' },
    }));

    expect(page.textContent).toContain('No se ha podido generar una de las respuestas. Hemos alcanzado el límite de uso');
    expect(arena.compareData()).toBeNull();
  });

  it('keeps the generic message for other errors', async () => {
    const { page } = await render(new HttpErrorResponse({ status: 500 }));

    expect(page.textContent).toContain('Error al comparar respuestas.');
  });
});
