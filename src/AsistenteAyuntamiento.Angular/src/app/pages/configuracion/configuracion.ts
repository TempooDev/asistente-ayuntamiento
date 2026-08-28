import { Component, OnInit, inject, signal, OnDestroy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule, DecimalPipe } from '@angular/common';
import { Subscription } from 'rxjs';
import { AiConfigService, SaveAiConfigurationDto } from '../../services/ai-config';
import { ScraperFilterService, ScraperFilterRuleDto, CreateFilterRuleDto, DocumentSource, FilterType } from '../../services/scraper-filter';
import { NotificationService } from '../../services/notification.service';

@Component({
  selector: 'app-configuracion',
  standalone: true,
  imports: [FormsModule, DecimalPipe, CommonModule],
  templateUrl: './configuracion.html',
  styleUrl: './configuracion.scss'
})
export class ConfiguracionComponent implements OnInit, OnDestroy {
  private aiClient = inject(AiConfigService);
  private scraperClient = inject(ScraperFilterService);
  private notificationService = inject(NotificationService);

  activeTab = signal<'ai' | 'scraper'>('ai');

  // Form Model AI
  config = signal<SaveAiConfigurationDto>({
    provider: 'ollama',
    model: 'llama3.2',
    temperature: 0.3
  });

  hasSavedApiKey = signal(false);
  isLoading = signal(true);
  isSaving = signal(false);
  showSuccessMessage = signal(false);
  showErrorMessage = signal(false);

  // Scraper State
  filters = signal<ScraperFilterRuleDto[]>([]);
  isLoadingFilters = signal(false);
  isTriggeringScrape = signal(false);
  scrapeMessage = signal<string>('');
  newFilter = signal<CreateFilterRuleDto>({ provider: DocumentSource.BOE, filterType: FilterType.Department, value: '' });

  // Manual Scrape State
  scrapeStartDate = signal<string>('');
  scrapeEndDate = signal<string>('');

  // Expose enums to template
  DocumentSource = DocumentSource;
  
  private scraperStateSub?: Subscription;

  async ngOnInit() {
    await this.cargarConfiguracion();
    await this.cargarFiltros();
    this.cargarScraperState();
    
    // Conectar SignalR de notificaciones del sistema
    await this.notificationService.connect();
    
    // Escuchar eventos en tiempo real
    this.scraperStateSub = this.notificationService.scraperStateChanged$.subscribe(state => {
      this.isTriggeringScrape.set(state.isScraping);
      this.scrapeMessage.set(state.message);
      
      if (!state.isScraping && state.message === "") {
         this.scrapeResultMessage.set({ type: 'success', text: 'Proceso de extracción finalizado correctamente.' });
         // Borrar mensaje al cabo de unos segundos
         setTimeout(() => this.scrapeResultMessage.set(null), 8000);
      }
    });
  }

  ngOnDestroy() {
    this.scraperStateSub?.unsubscribe();
  }

  cargarScraperState() {
    this.scraperClient.getState().subscribe({
      next: (state) => {
        this.isTriggeringScrape.set(state.isScraping);
        this.scrapeMessage.set(state.message);
      }
    });
  }

  async cargarConfiguracion() {
    this.isLoading.set(true);
    this.showSuccessMessage.set(false);
    this.showErrorMessage.set(false);

    try {
      const currentConfig = await this.aiClient.getConfiguration();
      this.config.set({
        provider: currentConfig.provider,
        model: currentConfig.model,
        temperature: currentConfig.temperature,
        endpointUrl: currentConfig.endpointUrl,
        apiKey: '' // Never show the real API key on the frontend
      });
      this.hasSavedApiKey.set(currentConfig.hasApiKey);
    } catch (e) {
      console.error(e);
      this.showErrorMessage.set(true);
    } finally {
      this.isLoading.set(false);
    }
  }

  async guardarConfiguracion() {
    this.isSaving.set(true);
    this.showSuccessMessage.set(false);
    this.showErrorMessage.set(false);

    try {
      const currentConfig = this.config();
      const payload = { ...currentConfig };
      if (!payload.apiKey) {
        delete payload.apiKey;
      }
      
      await this.aiClient.saveConfiguration(payload);
      this.showSuccessMessage.set(true);

      if (payload.apiKey) {
        this.hasSavedApiKey.set(true);
        this.config.update(c => ({ ...c, apiKey: '' }));
      }

      setTimeout(() => {
        this.showSuccessMessage.set(false);
      }, 3000);
    } catch (e) {
      console.error(e);
      this.showErrorMessage.set(true);
    } finally {
      this.isSaving.set(false);
    }
  }

  updateConfig(key: keyof SaveAiConfigurationDto, value: any) {
    this.config.update(c => ({ ...c, [key]: value }));
  }

  // SCRAPER LOGIC

  async cargarFiltros() {
    this.isLoadingFilters.set(true);
    this.scraperClient.getFilters().subscribe({
      next: (data) => this.filters.set(data),
      error: (e) => console.error(e),
      complete: () => this.isLoadingFilters.set(false)
    });
  }

  crearFiltro() {
    if (!this.newFilter().value) return;
    this.scraperClient.createFilter(this.newFilter()).subscribe({
      next: (rule) => {
        this.filters.update(f => [...f, rule]);
        this.newFilter.set({ provider: DocumentSource.BOE, filterType: FilterType.Department, value: '' });
      },
      error: (e) => console.error(e)
    });
  }

  toggleFiltro(filter: ScraperFilterRuleDto) {
    this.scraperClient.updateFilterStatus(filter.id, { isActive: !filter.isActive }).subscribe({
      next: () => {
        this.filters.update(f => f.map(x => x.id === filter.id ? { ...x, isActive: !x.isActive } : x));
      },
      error: (e) => console.error(e)
    });
  }

  eliminarFiltro(id: number) {
    this.scraperClient.deleteFilter(id).subscribe({
      next: () => {
        this.filters.update(f => f.filter(x => x.id !== id));
      },
      error: (e) => console.error(e)
    });
  }

  // BOJA specific feeds handling
  readonly BOJA_FEEDS = [
    { url: 'https://www.juntadeandalucia.es/boja/distribucion/s51.xml', label: '1. Disposiciones generales' },
    { url: 'https://www.juntadeandalucia.es/boja/distribucion/s52.xml', label: '2. Autoridades y personal' },
    { url: 'https://www.juntadeandalucia.es/boja/distribucion/s53.xml', label: '3. Otras disposiciones' },
    { url: 'https://www.juntadeandalucia.es/boja/distribucion/s54.xml', label: '4. Administración de Justicia' },
    { url: 'https://www.juntadeandalucia.es/boja/distribucion/s55.xml', label: '5. Anuncios' }
  ];

  hasBojaFeed(url: string): boolean {
    return this.filters().some(f => f.provider === DocumentSource.BOJA && f.filterType === FilterType.BojaFeed && f.value === url && f.isActive);
  }

  toggleBojaFeed(url: string, event: Event) {
    const isChecked = (event.target as HTMLInputElement).checked;
    const existingFilter = this.filters().find(f => f.provider === DocumentSource.BOJA && f.filterType === FilterType.BojaFeed && f.value === url);

    if (existingFilter) {
      this.scraperClient.updateFilterStatus(existingFilter.id, { isActive: isChecked }).subscribe({
        next: () => this.filters.update(f => f.map(x => x.id === existingFilter.id ? { ...x, isActive: isChecked } : x)),
        error: (e) => console.error(e)
      });
    } else if (isChecked) {
      this.scraperClient.createFilter({ provider: DocumentSource.BOJA, filterType: FilterType.BojaFeed, value: url }).subscribe({
        next: (rule) => this.filters.update(f => [...f, rule]),
        error: (e) => console.error(e)
      });
    }
  }

  scrapeResultMessage = signal<{ type: 'success' | 'error', text: string } | null>(null);

  forzarScrape(provider: DocumentSource) {
    // Optimistically update UI
    this.isTriggeringScrape.set(true);
    this.scrapeMessage.set(`Iniciando petición para ${provider}...`);
    this.scrapeResultMessage.set(null);
    
    const payload: any = { provider };
    if (this.scrapeStartDate()) payload.startDate = this.scrapeStartDate();
    if (this.scrapeEndDate()) payload.endDate = this.scrapeEndDate();

    this.scraperClient.triggerScrape(payload).subscribe({
      next: () => {
        // La actualización real vendrá por SignalR
      },
      error: (e) => {
        this.scrapeResultMessage.set({
          type: 'error',
          text: `Error al iniciar el scrape: ${e.error || e.message}`
        });
        this.isTriggeringScrape.set(false);
        this.scrapeMessage.set('');
      }
    });
  }
}
