import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NotificationService } from '../../../core/realtime/notification.service';
import { DocumentSource } from '../../../shared/models/document-source';
import { ToastService } from '../../../shared/ui/toast.service';
import { ScraperFilterService } from '../data-access/scraper-filter.service';
import { TriggerScrapeDto } from '../models/settings.models';

/** Starts a scrape for a date range; progress arrives over SignalR (ScraperStateChanged). */
@Component({
  selector: 'app-manual-scrape',
  templateUrl: './manual-scrape.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ManualScrapeComponent {
  private readonly api = inject(ScraperFilterService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly startDate = signal('');
  protected readonly endDate = signal('');
  protected readonly isScraping = signal(false);
  protected readonly progress = signal('');
  protected readonly boeSections = signal<readonly string[]>(['1', '2B', '3', '5A']);

  /** BOPMA is not offered: the scraper does not implement it yet. */
  protected readonly providers = [DocumentSource.BOE, DocumentSource.BOJA];
  protected readonly sections: readonly { value: string; label: string }[] = [
    { value: '1', label: 'Sección I (Disposiciones Generales)' },
    { value: '2B', label: 'Sección II.B (Autoridades y Personal)' },
    { value: '3', label: 'Sección III (Otras Disposiciones)' },
    { value: '5A', label: 'Sección V.A (Anuncios)' },
    { value: '5B', label: 'Sección V.B (Otros Anuncios)' },
  ];

  constructor() {
    const notifications = inject(NotificationService);
    void notifications.connect();
    notifications.scraperStateChanged$.pipe(takeUntilDestroyed()).subscribe(state => {
      const wasScraping = this.isScraping();
      this.isScraping.set(state.isScraping);
      this.progress.set(state.message);
      if (wasScraping && !state.isScraping && state.message === '') {
        this.toast.success('Proceso de extracción finalizado correctamente.');
      }
    });
    this.api
      .getState()
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: state => {
          this.isScraping.set(state.isScraping);
          this.progress.set(state.message);
        },
      });
  }

  protected toggleSection(value: string, checked: boolean): void {
    this.boeSections.update(list => (checked ? [...new Set([...list, value])] : list.filter(s => s !== value)));
  }

  protected start(provider: DocumentSource): void {
    this.isScraping.set(true);
    this.progress.set(`Iniciando petición para ${provider}...`);

    const request: TriggerScrapeDto = {
      provider,
      startDate: this.startDate() || undefined,
      endDate: this.endDate() || undefined,
      sections: provider === DocumentSource.BOE && this.boeSections().length > 0 ? [...this.boeSections()] : undefined,
    };
    this.api
      .triggerScrape(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        // Progress and completion arrive over SignalR.
        error: (err: { error?: { message?: string; detail?: string }; message?: string }) => {
          // 409 → { message }, 503 → ProblemDetails { detail }
          this.toast.error(`Error al iniciar el scrape: ${err.error?.message ?? err.error?.detail ?? err.message}`);
          this.isScraping.set(false);
          this.progress.set('');
        },
      });
  }
}
