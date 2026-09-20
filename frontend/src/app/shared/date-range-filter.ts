import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FilterService, PRESETS, PresetId, toIso } from '../core/filter.service';

/** The period selector shown on every page: presets plus an explicit start and end date. */
@Component({
  selector: 'cx-date-range-filter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="row" role="group" aria-label="Reporting period">
      <label class="field">
        Period
        <select [value]="filter.preset()" (change)="onPreset($any($event.target).value)">
          @for (p of presets; track p.id) {
            <option [value]="p.id" [selected]="p.id === filter.preset()">{{ p.label }}</option>
          }
        </select>
      </label>
      <label class="field">
        Start date
        <input type="date" [value]="start()" [max]="today" (change)="onDate('start', $any($event.target).value)" />
      </label>
      <label class="field">
        End date
        <input type="date" [value]="end()" [max]="today" (change)="onDate('end', $any($event.target).value)" />
      </label>
    </div>
    @if (error()) {
      <p class="notice error" role="alert">{{ error() }}</p>
    }
  `,
  styles: `
    :host { display: block; }
    .row { display: flex; flex-wrap: wrap; gap: 12px; align-items: end; }
    .field { min-width: 150px; }
    .notice { margin-top: 8px; }
  `,
})
export class DateRangeFilter {
  protected readonly filter = inject(FilterService);
  protected readonly presets = PRESETS;
  protected readonly today = toIso(new Date());
  protected readonly error = signal<string | null>(null);
  protected readonly start = computed(() => this.filter.range().start);
  protected readonly end = computed(() => this.filter.range().end);

  protected onPreset(id: PresetId): void {
    this.error.set(null);
    this.filter.setPreset(id);
  }

  protected onDate(which: 'start' | 'end', value: string): void {
    const r = this.filter.range();
    this.error.set(this.filter.setCustom(which === 'start' ? value : r.start, which === 'end' ? value : r.end));
  }
}
