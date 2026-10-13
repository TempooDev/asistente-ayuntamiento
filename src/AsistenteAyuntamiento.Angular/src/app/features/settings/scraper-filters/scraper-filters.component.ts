import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { DocumentSource } from '../../../shared/models/document-source';
import { ToastService } from '../../../shared/ui/toast.service';
import { ScraperFilterService } from '../data-access/scraper-filter.service';
import { FilterType, ScraperFilterRuleDto } from '../models/settings.models';

/** BOJA feed selection and the filter rules the scraper applies. */
@Component({
  selector: 'app-scraper-filters',
  imports: [ReactiveFormsModule],
  templateUrl: './scraper-filters.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ScraperFiltersComponent implements OnInit {
  private readonly api = inject(ScraperFilterService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly filters = signal<readonly ScraperFilterRuleDto[]>([]);
  protected readonly isLoading = signal(false);
  protected readonly newFilter = inject(NonNullableFormBuilder).group({
    provider: [DocumentSource.BOE as DocumentSource],
    filterType: [FilterType.Department],
    value: ['', Validators.required],
  });

  protected readonly providers = [DocumentSource.BOE, DocumentSource.BOJA];
  protected readonly filterTypes: readonly { value: FilterType; label: string }[] = [
    { value: FilterType.Department, label: 'Departamento' },
    { value: FilterType.Section, label: 'Sección' },
    { value: FilterType.Keyword, label: 'Palabra Clave' },
    { value: FilterType.BojaFeed, label: 'Feed de BOJA (URL)' },
  ];
  protected readonly bojaFeeds: readonly { url: string; label: string }[] = [
    { url: 'https://www.juntadeandalucia.es/boja/distribucion/s51.xml', label: '1. Disposiciones generales' },
    { url: 'https://www.juntadeandalucia.es/boja/distribucion/s52.xml', label: '2. Autoridades y personal' },
    { url: 'https://www.juntadeandalucia.es/boja/distribucion/s53.xml', label: '3. Otras disposiciones' },
    { url: 'https://www.juntadeandalucia.es/boja/distribucion/s54.xml', label: '4. Administración de Justicia' },
    { url: 'https://www.juntadeandalucia.es/boja/distribucion/s55.xml', label: '5. Anuncios' },
  ];

  ngOnInit(): void {
    this.isLoading.set(true);
    this.api
      .getFilters()
      .pipe(
        finalize(() => this.isLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: rules => this.filters.set(rules),
        error: () => this.toast.error('No se han podido cargar los filtros.'),
      });
  }

  protected isFeedActive(url: string): boolean {
    return this.filters().some(f => f.provider === DocumentSource.BOJA && f.filterType === FilterType.BojaFeed && f.value === url && f.isActive);
  }

  protected toggleFeed(url: string, active: boolean): void {
    const existing = this.filters().find(f => f.provider === DocumentSource.BOJA && f.filterType === FilterType.BojaFeed && f.value === url);
    if (existing) {
      this.setActive(existing, active);
    } else if (active) {
      this.create({ provider: DocumentSource.BOJA, filterType: FilterType.BojaFeed, value: url });
    }
  }

  protected add(): void {
    if (this.newFilter.invalid) return;
    this.create(this.newFilter.getRawValue(), () => this.newFilter.reset());
  }

  protected toggle(rule: ScraperFilterRuleDto): void {
    this.setActive(rule, !rule.isActive);
  }

  protected remove(rule: ScraperFilterRuleDto): void {
    this.api
      .deleteFilter(rule.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.filters.update(list => list.filter(f => f.id !== rule.id)),
        error: () => this.toast.error('No se ha podido eliminar el filtro.'),
      });
  }

  private create(rule: { provider: DocumentSource; filterType: FilterType; value: string }, onDone?: () => void): void {
    this.api
      .createFilter(rule)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: created => {
          this.filters.update(list => [...list, created]);
          onDone?.();
        },
        error: () => this.toast.error('No se ha podido crear el filtro.'),
      });
  }

  private setActive(rule: ScraperFilterRuleDto, isActive: boolean): void {
    this.api
      .updateFilterStatus(rule.id, { isActive })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.filters.update(list => list.map(f => (f.id === rule.id ? { ...f, isActive } : f))),
        error: () => this.toast.error('No se ha podido actualizar el filtro.'),
      });
  }
}
