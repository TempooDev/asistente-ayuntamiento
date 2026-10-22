import { DateRange } from '../models/analytics.models';

export type PeriodPreset = '7d' | '30d' | 'custom';

export interface PeriodSelection {
  preset: PeriodPreset;
  /** Custom start and end dates (YYYY-MM-DD), both inclusive. */
  start: string;
  end: string;
}

export interface Periods {
  current: DateRange;
  previous: DateRange;
  days: number;
}

const DAY_MS = 86_400_000;

export function isoDate(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function addDays(iso: string, days: number): string {
  return isoDate(new Date(Date.parse(`${iso}T00:00:00Z`) + days * DAY_MS));
}

function daysBetween(from: string, to: string): number {
  return Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / DAY_MS);
}

/**
 * Current period (UTC dates, `to` exclusive, today included) and the previous period of the same length.
 * Null when a custom range is incomplete or inverted.
 */
export function resolvePeriods(selection: PeriodSelection, today: Date = new Date()): Periods | null {
  let current: DateRange;
  if (selection.preset === 'custom') {
    if (!selection.start || !selection.end || selection.start > selection.end) return null;
    current = { from: selection.start, to: addDays(selection.end, 1) };
  } else {
    const to = addDays(isoDate(today), 1);
    current = { from: addDays(to, selection.preset === '7d' ? -7 : -30), to };
  }
  const days = daysBetween(current.from, current.to);
  return { current, previous: { from: addDays(current.from, -days), to: current.from }, days };
}

/** Change in percent against the previous period; null when there is nothing to compare with. */
export function percentChange(current: number | null | undefined, previous: number | null | undefined): number | null {
  if (current == null || previous == null || previous === 0) return null;
  return ((current - previous) / previous) * 100;
}
