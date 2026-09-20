import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export interface AnswerRow {
  key: string;
  label: string;
  tone: 'ok' | 'info' | 'warn' | 'bad';
}

/** How customers answered a survey question: label, proportional bar, count and share. Not colour-dependent. */
@Component({
  selector: 'cx-answer-bars',
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul>
      @for (r of rows(); track r.key) {
        <li [class]="'tone-' + r.tone">
          <span class="label">{{ r.label }}</span>
          <span class="track" aria-hidden="true"><span class="bar" [style.width.%]="share(r.key)"></span></span>
          <span class="count num">{{ counts()[r.key] ?? 0 }} <span class="muted">({{ share(r.key) | number: '1.0-0' }}%)</span></span>
        </li>
      }
    </ul>
    @if (total() === 0) {
      <p class="muted">No responses in this period.</p>
    }
  `,
  styles: `
    ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 10px; }
    li { display: grid; grid-template-columns: 150px 1fr 84px; align-items: center; gap: 10px; background: none; }
    .label { font-size: 0.875rem; color: var(--text); }
    .track { height: 10px; background: var(--surface-2); border-radius: 999px; overflow: hidden; }
    .bar { display: block; height: 100%; border-radius: 999px; background: currentColor; }
    .count { text-align: right; font-size: 0.875rem; color: var(--text); }
    li.tone-ok { color: var(--ok); }
    li.tone-info { color: var(--info); }
    li.tone-warn { color: var(--warn); }
    li.tone-bad { color: var(--bad); }
    @media (max-width: 480px) { li { grid-template-columns: 110px 1fr 72px; } }
  `,
})
export class AnswerBars {
  readonly rows = input.required<AnswerRow[]>();
  readonly counts = input.required<Record<string, number>>();
  protected readonly total = computed(() => Object.values(this.counts()).reduce((a, b) => a + b, 0));

  protected share(key: string): number {
    const total = this.total();
    return total === 0 ? 0 : ((this.counts()[key] ?? 0) / total) * 100;
  }
}
