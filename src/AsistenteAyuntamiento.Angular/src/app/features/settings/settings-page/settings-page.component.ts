import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { AiConfigFormComponent } from '../ai-config-form/ai-config-form.component';
import { ManualScrapeComponent } from '../manual-scrape/manual-scrape.component';
import { ScraperFiltersComponent } from '../scraper-filters/scraper-filters.component';

type Tab = 'ai' | 'scraper';

@Component({
  selector: 'app-settings-page',
  imports: [AiConfigFormComponent, ScraperFiltersComponent, ManualScrapeComponent],
  templateUrl: './settings-page.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingsPageComponent {
  protected readonly activeTab = signal<Tab>('ai');
  protected readonly tabs: readonly { id: Tab; label: string }[] = [
    { id: 'ai', label: 'Configuración de IA' },
    { id: 'scraper', label: 'Filtros del Scraper' },
  ];
}
