const LOCALE = 'es-ES';

/** Formatted number, or "—" when the value is missing. */
export function formatNumber(value: number | null | undefined, maxDigits = 0): string {
  if (value == null || Number.isNaN(value)) return '—';
  return new Intl.NumberFormat(LOCALE, { maximumFractionDigits: maxDigits, useGrouping: true }).format(value);
}

/** Milliseconds as "850 ms" or "2,8 s"; "—" when missing. */
export function formatMs(value: number | null | undefined): string {
  if (value == null || Number.isNaN(value)) return '—';
  return value < 1000 ? `${formatNumber(value)} ms` : `${formatNumber(value / 1000, 1)} s`;
}

/** Euros with up to 4 decimals (small per-call costs); "—" when missing. */
export function formatEur(value: number | null | undefined): string {
  if (value == null || Number.isNaN(value)) return '—';
  return `${new Intl.NumberFormat(LOCALE, { minimumFractionDigits: 2, maximumFractionDigits: 4 }).format(value)} €`;
}

/** A 0–1 ratio as a percentage ("72 %"); "—" when missing. */
export function formatRatio(value: number | null | undefined): string {
  return value == null ? '—' : `${formatNumber(value * 100, 1)} %`;
}

/** A value that is already a percentage (0–100); "—" when missing. */
export function formatPercent(value: number | null | undefined): string {
  return value == null ? '—' : `${formatNumber(value, 1)} %`;
}

export function formatChange(value: number | null): string {
  if (value == null) return '';
  return `${value > 0 ? '+' : ''}${formatNumber(value, 1)} %`;
}
