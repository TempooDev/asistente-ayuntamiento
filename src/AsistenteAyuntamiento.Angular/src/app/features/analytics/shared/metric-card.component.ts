import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { formatChange } from './format';

/** A metric card: label, value ("—" when missing), optional detail and change against the previous period. */
@Component({
  selector: 'app-metric-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block bg-surface-container-lowest p-5 rounded-xl shadow-xs border border-gray-100' },
  template: `
    <h3 class="text-gray-500 text-sm font-medium" [attr.title]="hint() || null">{{ label() }}</h3>
    <p class="text-2xl font-bold text-gray-900 mt-1" data-testid="metric-value">
      {{ value() ?? "—" }}
    </p>
    <ng-content />
    @if (change() !== null) {
      <p class="text-xs mt-1" [class]="changeClass()" data-testid="metric-change">
        {{ formatChange(change()) }} vs periodo anterior
      </p>
    }
  `,
})
export class MetricCardComponent {
  readonly label = input.required<string>();
  /** Already formatted value; null shows "—". */
  readonly value = input<string | null>(null);
  readonly change = input<number | null>(null);
  readonly hint = input('');
  /** For metrics where lower is better (latency, cost), a rise is shown in red. */
  readonly lowerIsBetter = input(false);

  protected readonly formatChange = formatChange;

  protected readonly changeClass = computed(() => {
    const c = this.change() ?? 0;
    if (c === 0) return 'text-gray-500';
    const good = this.lowerIsBetter() ? c < 0 : c > 0;
    return good ? 'text-green-700 dark:text-green-300' : 'text-red-700 dark:text-red-300';
  });
}
