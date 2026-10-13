import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { AuthFacade } from '../../../core/auth/auth-facade.service';
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
  private readonly auth = inject(AuthFacade);

  /** AI settings belong to the organization; scraper filters to the shared knowledge base (platform). */
  protected readonly tabs = computed(() => [
    ...(this.auth.isOrganizationAdmin() ? [{ id: 'ai' as Tab, label: 'Configuración de IA' }] : []),
    ...(this.auth.isPlatformAdmin() ? [{ id: 'scraper' as Tab, label: 'Filtros del Scraper' }] : []),
  ]);

  private readonly selectedTab = signal<Tab | null>(null);

  /** The chosen tab, or the first one the user may see. */
  protected readonly activeTab = computed(() => {
    const available = this.tabs().map(t => t.id);
    const selected = this.selectedTab();
    return selected && available.includes(selected) ? selected : available[0] ?? null;
  });

  protected selectTab(tab: Tab): void {
    this.selectedTab.set(tab);
  }
}
