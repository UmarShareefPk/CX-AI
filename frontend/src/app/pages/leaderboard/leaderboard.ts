import { DecimalPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, linkedSignal } from '@angular/core';
import { Router } from '@angular/router';
import { FilterService } from '../../core/filter.service';
import { Leaderboard } from '../../core/models';
import { scoreBand, toneClass } from '../../core/score';

@Component({
  selector: 'cx-leaderboard',
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div>
        <h1>Dealer leaderboard</h1>
        <p class="muted">
          Dealers ranked by score for {{ filter.label() }}. Score = total net score ÷ number of surveys. Equal scores share a rank.
        </p>
      </div>

      <section class="card" [class.stale]="resource.isLoading()">
        @if (resource.error() && !data()) {
          <p class="notice error" role="alert">
            The leaderboard could not be loaded.
            <button class="btn btn-sm" type="button" (click)="resource.reload()">Retry</button>
          </p>
        } @else if (data(); as d) {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">Dealer leaderboard</caption>
              <thead>
                <tr>
                  <th scope="col">Rank</th>
                  <th scope="col">Dealer</th>
                  <th scope="col" class="right">Surveys</th>
                  <th scope="col">Score</th>
                  <th scope="col">Rating</th>
                  <th scope="col"><span class="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (r of d.dealers; track r.dealerId) {
                  <tr>
                    <td class="num rank">{{ r.rank ?? '—' }}</td>
                    <td>{{ r.dealerName }}<br /><span class="muted">{{ r.dealerId }}</span></td>
                    <td class="right num">{{ r.surveyCount }}</td>
                    <td>
                      <div class="scorecell">
                        @if (r.score !== null) {
                          <span class="bar" aria-hidden="true"><span [class]="'fill ' + tone(r.score)" [style.width.%]="r.score"></span></span>
                          <span class="num">{{ r.score | number: '1.0-2' }}</span>
                        } @else {
                          <span class="muted">No surveys</span>
                        }
                      </div>
                    </td>
                    <td><span [class]="'chip ' + tone(r.score)">{{ band(r.score) }}</span></td>
                    <td class="right">
                      <button class="btn btn-sm" type="button" (click)="open(r.dealerId)">Open dashboard</button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (ties()) {
            <p class="muted note">Dealers with the same score share a rank; the next rank is skipped.</p>
          }
        } @else {
          <p class="muted" role="status">Loading leaderboard…</p>
        }
      </section>
    </div>
  `,
  styles: `
    .stale { opacity: 0.65; transition: opacity 0.15s; }
    .rank { font-weight: 700; font-size: 1.05rem; width: 64px; }
    .scorecell { min-width: 220px; display: flex; align-items: center; gap: 10px; }
    .bar { flex: 1; height: 8px; border-radius: 999px; background: var(--surface-2); overflow: hidden; min-width: 80px; }
    .fill { display: block; height: 100%; background: currentColor; }
    .note { padding-top: 12px; }
  `,
})
export class LeaderboardPage {
  protected readonly filter = inject(FilterService);
  private readonly router = inject(Router);

  protected readonly resource = httpResource<Leaderboard>(() => {
    const { start, end } = this.filter.range();
    return { url: '/api/leaderboard', params: { start, end } };
  });
  protected readonly data = linkedSignal<Leaderboard | undefined, Leaderboard | undefined>({
    source: () => this.resource.value(),
    computation: (value, previous) => value ?? previous?.value,
  });
  protected readonly ties = computed(() => {
    const ranks = (this.data()?.dealers ?? []).map((d) => d.rank).filter((r) => r !== null);
    return new Set(ranks).size !== ranks.length;
  });

  protected band = (score: number | null) => scoreBand(score).label;
  protected tone = (score: number | null) => toneClass(scoreBand(score).tone);

  protected open(dealerId: string): void {
    this.filter.dealerId.set(dealerId);
    void this.router.navigate(['/dashboard']);
  }
}
