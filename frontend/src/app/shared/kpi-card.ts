import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'cx-kpi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="card">
      <h3 class="muted">{{ label() }}</h3>
      <p class="value num">{{ value() }}<span class="unit muted">{{ unit() }}</span></p>
      <ng-content />
    </article>
  `,
  styles: `
    article { height: 100%; display: grid; gap: 6px; align-content: start; }
    h3 { font-size: 0.8125rem; text-transform: uppercase; letter-spacing: 0.04em; }
    .value { font-size: 2.25rem; font-weight: 700; line-height: 1.1; }
    .unit { font-size: 1rem; font-weight: 600; margin-left: 4px; }
  `,
})
export class Kpi {
  readonly label = input.required<string>();
  readonly value = input.required<string>();
  readonly unit = input('');
}
