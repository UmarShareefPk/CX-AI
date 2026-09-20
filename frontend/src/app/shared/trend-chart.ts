import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, effect, inject, input, viewChild } from '@angular/core';
import { CategoryScale, Chart, Filler, LineController, LineElement, LinearScale, PointElement, Tooltip } from 'chart.js';
import { TrendPoint } from '../core/models';
import { fromIso } from '../core/filter.service';

Chart.register(LineController, LineElement, PointElement, CategoryScale, LinearScale, Tooltip, Filler);

const css = (name: string) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

/** Score over time. One point per day (short ranges) or per week (long ranges), 0-100 scale. */
@Component({
  selector: 'cx-trend-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div class="box"><canvas #canvas role="img" [attr.aria-label]="summary()"></canvas></div>`,
  styles: `
    :host { display: block; }
    .box { position: relative; height: 260px; }
  `,
})
export class TrendChart {
  readonly points = input.required<TrendPoint[]>();
  readonly weekly = input(false);
  readonly summary = input('Score trend');

  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private chart?: Chart;

  constructor() {
    effect(() => {
      const points = this.points();
      const weekly = this.weekly();
      this.chart?.destroy();

      const label = (p: TrendPoint) =>
        (weekly ? 'Week of ' : '') + fromIso(p.date).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
      const brand = css('--brand');
      const muted = css('--muted');
      const grid = css('--border');

      this.chart = new Chart(this.canvas().nativeElement, {
        type: 'line',
        data: {
          labels: points.map(label),
          datasets: [
            {
              data: points.map((p) => p.score),
              borderColor: brand,
              backgroundColor: brand + '22',
              pointBackgroundColor: brand,
              pointRadius: points.length > 40 ? 2 : 4,
              tension: 0.25,
              fill: true,
            },
          ],
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          animation: false,
          scales: {
            y: { min: 0, max: 100, ticks: { color: muted, stepSize: 25 }, grid: { color: grid } },
            x: { ticks: { color: muted, maxRotation: 0, autoSkip: true, maxTicksLimit: 8 }, grid: { display: false } },
          },
          plugins: {
            legend: { display: false },
            tooltip: {
              callbacks: {
                label: (ctx) => `Score ${Number(ctx.parsed.y).toFixed(2)}`,
                afterLabel: (ctx) => `${points[ctx.dataIndex].surveyCount} surveys`,
              },
            },
          },
        },
      });
    });

    inject(DestroyRef).onDestroy(() => this.chart?.destroy());
  }
}
