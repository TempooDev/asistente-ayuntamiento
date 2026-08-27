import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule, DecimalPipe } from '@angular/common';
import { AiConfigService, SaveAiConfigurationDto } from '../../services/ai-config';
import { ScraperFilterService, ScraperFilterRuleDto, CreateFilterRuleDto } from '../../services/scraper-filter';

@Component({
  selector: 'app-configuracion',
  standalone: true,
  imports: [FormsModule, DecimalPipe, CommonModule],
  templateUrl: './configuracion.html',
  styleUrl: './configuracion.scss'
})
export class ConfiguracionComponent implements OnInit {
  private aiClient = inject(AiConfigService);
  private scraperClient = inject(ScraperFilterService);

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
  newFilter = signal<CreateFilterRuleDto>({ provider: 'BOE', filterType: 'Department', value: '' });

  async ngOnInit() {
    await this.cargarConfiguracion();
    await this.cargarFiltros();
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
        this.newFilter.set({ provider: 'BOE', filterType: 'Department', value: '' });
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

  forzarScrape(provider: string) {
    this.isTriggeringScrape.set(true);
    this.scraperClient.triggerScrape({ provider }).subscribe({
      next: (res: any) => {
        alert(`Scrape finalizado. Éxito: ${res.success}. Items extraídos: ${res.itemsExtracted}`);
        this.isTriggeringScrape.set(false);
      },
      error: (e) => {
        alert(`Error al forzar el scrape: ${e.message}`);
        this.isTriggeringScrape.set(false);
      }
    });
  }
}
