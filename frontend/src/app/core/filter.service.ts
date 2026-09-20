import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { AuthService } from './auth.service';

export type PresetId = 'this_month' | 'last_month' | 'this_quarter' | 'last_quarter' | 'last_30_days' | 'last_6_months' | 'custom';

export const PRESETS: { id: PresetId; label: string }[] = [
  { id: 'this_month', label: 'This month' },
  { id: 'last_month', label: 'Last month' },
  { id: 'this_quarter', label: 'This quarter' },
  { id: 'last_quarter', label: 'Last quarter' },
  { id: 'last_30_days', label: 'Last 30 days' },
  { id: 'last_6_months', label: 'Last 6 months' },
  { id: 'custom', label: 'Custom range' },
];

export interface DateRangeValue {
  start: string; // yyyy-MM-dd
  end: string;
}

const STORAGE_KEY = 'cx.filter';

export function toIso(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export function fromIso(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d);
}

/** Mirrors the server's named periods (Cx.Core PeriodResolver) so the two always agree. */
export function presetRange(id: Exclude<PresetId, 'custom'>, today = new Date()): DateRangeValue {
  const y = today.getFullYear();
  const m = today.getMonth();
  const quarterStart = Math.floor(m / 3) * 3;
  const day = (year: number, month: number, dom: number) => new Date(year, month, dom);
  switch (id) {
    case 'this_month':
      return { start: toIso(day(y, m, 1)), end: toIso(today) };
    case 'last_month':
      return { start: toIso(day(y, m - 1, 1)), end: toIso(day(y, m, 0)) };
    case 'this_quarter':
      return { start: toIso(day(y, quarterStart, 1)), end: toIso(today) };
    case 'last_quarter':
      return { start: toIso(day(y, quarterStart - 3, 1)), end: toIso(day(y, quarterStart, 0)) };
    case 'last_30_days':
      return { start: toIso(day(y, m, today.getDate() - 29)), end: toIso(today) };
    case 'last_6_months':
      return { start: toIso(day(y, m - 5, 1)), end: toIso(today) };
  }
}

/**
 * The single source of truth for the selected period (and, for administrators, the selected dealer).
 * Every page reads it, so "only the selected period's data is shown" holds everywhere.
 */
@Injectable({ providedIn: 'root' })
export class FilterService {
  private readonly auth = inject(AuthService);
  private readonly saved = this.restore();

  readonly preset = signal<PresetId>(this.saved?.preset ?? 'this_month');
  readonly range = signal<DateRangeValue>(this.saved?.range ?? presetRange('this_month'));
  /** Administrators only: narrow every page to one dealer. Null means all dealers. */
  readonly dealerId = signal<string | null>(null);

  /** Query-string parameters shared by every data request. */
  readonly params = computed(() => {
    const r = this.range();
    const dealer = this.dealerId();
    const params: Record<string, string> = { start: r.start, end: r.end };
    if (dealer) params['dealerId'] = dealer;
    return params;
  });

  readonly label = computed(() => {
    const fmt = (iso: string) => fromIso(iso).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
    const r = this.range();
    return `${fmt(r.start)} – ${fmt(r.end)}`;
  });

  constructor() {
    // A different user must never inherit the previous user's dealer selection.
    effect(() => {
      this.auth.user();
      this.dealerId.set(null);
    });
    effect(() => this.persist({ preset: this.preset(), range: this.range() }));
  }

  setPreset(id: PresetId): void {
    this.preset.set(id);
    if (id !== 'custom') this.range.set(presetRange(id));
  }

  /** Returns an error message, or null when the range was applied. */
  setCustom(start: string, end: string): string | null {
    if (!start || !end) return 'Choose both a start and an end date.';
    if (start > end) return 'The start date must not be after the end date.';
    this.preset.set('custom');
    this.range.set({ start, end });
    return null;
  }

  private persist(value: { preset: PresetId; range: DateRangeValue }): void {
    try {
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(value));
    } catch {
      /* not persisted */
    }
  }

  private restore(): { preset: PresetId; range: DateRangeValue } | null {
    try {
      const value = JSON.parse(sessionStorage.getItem(STORAGE_KEY) ?? 'null');
      // Named presets are recomputed so "This month" is still the current month tomorrow.
      if (value?.preset && value.preset !== 'custom') return { preset: value.preset, range: presetRange(value.preset) };
      return value?.range?.start && value?.range?.end ? value : null;
    } catch {
      return null;
    }
  }
}
