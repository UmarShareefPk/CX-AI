import { DecimalPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AiPanel } from '../../ai/ai-panel';
import { AuthService } from '../../core/auth.service';
import { FilterService } from '../../core/filter.service';
import { CONDITION_LABELS, RECOMMEND_LABELS, Summary, TrendPoint } from '../../core/models';
import { scoreBand, toneClass } from '../../core/score';
import { AnswerBars, AnswerRow } from '../../shared/answer-bars';
import { Kpi } from '../../shared/kpi-card';
import { TrendChart } from '../../shared/trend-chart';

const RECOMMEND_ROWS: AnswerRow[] = [
  { key: 'HighlyRecommend', label: RECOMMEND_LABELS['HighlyRecommend'], tone: 'ok' },
  { key: 'Recommend', label: RECOMMEND_LABELS['Recommend'], tone: 'info' },
  { key: 'MightRecommend', label: RECOMMEND_LABELS['MightRecommend'], tone: 'warn' },
  { key: 'NotRecommend', label: RECOMMEND_LABELS['NotRecommend'], tone: 'bad' },
];

const CONDITION_ROWS: AnswerRow[] = [
  { key: 'AllGood', label: CONDITION_LABELS['AllGood'], tone: 'ok' },
  { key: 'PartMissing', label: CONDITION_LABELS['PartMissing'], tone: 'warn' },
  { key: 'Defect', label: CONDITION_LABELS['Defect'], tone: 'warn' },
  { key: 'PartMissingAndDefect', label: CONDITION_LABELS['PartMissingAndDefect'], tone: 'bad' },
];

@Component({
  selector: 'cx-dashboard',
  imports: [DecimalPipe, RouterLink, Kpi, TrendChart, AnswerBars, AiPanel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page" [class.stale]="summary.isLoading()">
      <div class="head">
        <div>
          <h1>{{ title() }}</h1>
          <p class="muted">Survey results for {{ filter.label() }}</p>
        </div>
        <button class="btn btn-primary" type="button" (click)="aiOpen.set(true)" [attr.aria-expanded]="aiOpen()" aria-controls="ai-panel">
          <span aria-hidden="true">✦</span> Ask AI
        </button>
      </div>

      @if (summary.error() && !data()) {
        <div class="notice error" role="alert">
          The dashboard could not be loaded.
          <button class="btn btn-sm" type="button" (click)="summary.reload()">Retry</button>
        </div>
      } @else if (data(); as s) {
        <section class="grid-kpi" aria-label="Key indicators">
          <cx-kpi [label]="network() ? 'Network score' : 'Score'" [value]="fmt(s.score)" unit="/ 100">
            <p><span [class]="'chip ' + tone(s.score)">{{ band(s.score) }}</span></p>
            @if (s.scoreChange !== null && s.previousScore !== null) {
              <p class="delta" [class.up]="s.scoreChange > 0" [class.down]="s.scoreChange < 0">
                <span aria-hidden="true">{{ s.scoreChange > 0 ? '▲' : s.scoreChange < 0 ? '▼' : '■' }}</span>
                {{ s.scoreChange > 0 ? 'Up' : s.scoreChange < 0 ? 'Down' : 'Unchanged' }}
                @if (s.scoreChange !== 0) { {{ abs(s.scoreChange) | number: '1.0-2' }} }
                vs previous period ({{ s.previousScore | number: '1.0-2' }})
              </p>
            } @else {
              <p class="muted">No previous-period surveys to compare.</p>
            }
          </cx-kpi>

          @if (network()) {
            <cx-kpi label="Top dealer" [value]="fmt(s.topScore)" unit="/ 100">
              <p><strong>{{ s.topDealerName ?? '—' }}</strong></p>
              <a routerLink="/leaderboard" class="muted">View full leaderboard</a>
            </cx-kpi>
          } @else {
            <cx-kpi label="Rank" [value]="s.rank ? '#' + s.rank : '—'" [unit]="s.rank ? 'of ' + s.rankedDealerCount : ''">
              @if (s.rank === null) {
                <p class="muted">No surveys in this period, so not ranked.</p>
              } @else if (s.rank === 1) {
                <p><span class="chip tone-ok">Top ranked</span></p>
              } @else {
                <p class="muted">{{ gap(s) | number: '1.0-2' }} points behind #1</p>
              }
            </cx-kpi>
          }

          <cx-kpi label="Surveys received" [value]="'' + s.surveyCount">
            <p class="muted">{{ s.period.days }} days</p>
          </cx-kpi>

          @if (network()) {
            <cx-kpi label="Dealers ranked" [value]="'' + s.rankedDealerCount">
              <p class="muted">with at least one survey</p>
            </cx-kpi>
          } @else {
            <cx-kpi label="Top score in network" [value]="fmt(s.topScore)" unit="/ 100">
              <p class="muted">Benchmark for this period</p>
            </cx-kpi>
          }
        </section>

        <section class="card" aria-labelledby="trend-h">
          <header>
            <h2 id="trend-h">Score trend</h2>
            <span class="muted">{{ s.period.days > 45 ? 'Weekly' : 'Daily' }} average net score</span>
          </header>
          @if (trendPoints().length) {
            <cx-trend-chart [points]="trendPoints()" [weekly]="s.period.days > 45"
                            [summary]="'Score trend chart with ' + trendPoints().length + ' points'" />
          } @else {
            <p class="empty">No surveys in this period.</p>
          }
        </section>

        <div class="grid-charts">
          <section class="card" aria-labelledby="q1-h">
            <header>
              <h2 id="q1-h">Would customers recommend Honda?</h2>
              @if (weakest(s) === 'recommend') { <span class="chip tone-warn">Weakest area</span> }
            </header>
            <p class="muted avg">Average score {{ fmt(s.avgRecommendScore) }} / 100</p>
            <cx-answer-bars [rows]="recommendRows" [counts]="s.recommendBreakdown" />
          </section>
          <section class="card" aria-labelledby="q2-h">
            <header>
              <h2 id="q2-h">Any missing part or defect?</h2>
              @if (weakest(s) === 'condition') { <span class="chip tone-warn">Weakest area</span> }
            </header>
            <p class="muted avg">Average score {{ fmt(s.avgConditionScore) }} / 100</p>
            <cx-answer-bars [rows]="conditionRows" [counts]="s.conditionBreakdown" />
          </section>
        </div>
      } @else {
        <p class="muted" role="status">Loading dashboard…</p>
      }
    </div>

    <cx-ai-panel [(open)]="aiOpen" />
  `,
  styles: `
    .head { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; flex-wrap: wrap; }
    .stale { opacity: 0.65; transition: opacity 0.15s; }
    .delta { font-size: 0.875rem; }
    .delta.up { color: var(--ok); }
    .delta.down { color: var(--bad); }
    .avg { margin-bottom: 12px; }
    a { font-size: 0.875rem; }
  `,
})
export class Dashboard {
  protected readonly auth = inject(AuthService);
  protected readonly filter = inject(FilterService);
  protected readonly aiOpen = signal(false);

  protected readonly summary = httpResource<Summary>(() => ({ url: '/api/dashboard/summary', params: this.filter.params() }));
  private readonly trend = httpResource<TrendPoint[]>(() => ({ url: '/api/dashboard/trend', params: this.filter.params() }));

  /** Keeps showing the previous numbers (dimmed) while a new period loads, instead of flashing an empty page. */
  protected readonly data = linkedSignal<Summary | undefined, Summary | undefined>({
    source: () => this.summary.value(),
    computation: (value, previous) => value ?? previous?.value,
  });
  protected readonly trendPoints = computed(() => this.trend.value() ?? []);

  protected readonly network = computed(() => this.auth.isAdmin() && !this.filter.dealerId());
  protected readonly title = computed(() =>
    this.network() ? 'Network overview' : (this.data()?.dealerName ?? this.auth.user()?.displayName ?? 'Dashboard'),
  );

  protected readonly recommendRows = RECOMMEND_ROWS;
  protected readonly conditionRows = CONDITION_ROWS;

  protected fmt(v: number | null | undefined): string {
    return v === null || v === undefined ? '—' : v.toFixed(2).replace(/\.?0+$/, '');
  }
  protected band = (score: number | null) => scoreBand(score).label;
  protected tone = (score: number | null) => toneClass(scoreBand(score).tone);
  protected abs = Math.abs;
  protected gap = (s: Summary) => (s.topScore ?? 0) - (s.score ?? 0);

  protected weakest(s: Summary): 'recommend' | 'condition' | null {
    if (s.avgRecommendScore === null || s.avgConditionScore === null || s.avgRecommendScore === s.avgConditionScore) return null;
    return s.avgRecommendScore < s.avgConditionScore ? 'recommend' : 'condition';
  }
}
