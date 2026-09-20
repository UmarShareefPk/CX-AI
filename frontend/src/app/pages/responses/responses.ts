import { DatePipe, DecimalPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { AuthService } from '../../core/auth.service';
import { FilterService } from '../../core/filter.service';
import { CONDITION_LABELS, Paged, RECOMMEND_LABELS, SurveyRow } from '../../core/models';
import { scoreBand, toneClass } from '../../core/score';

type SortBy = 'submittedAt' | 'netScore';

@Component({
  selector: 'cx-responses',
  imports: [DatePipe, DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div>
        <h1>Survey responses</h1>
        <p class="muted">
          {{ total() }} {{ total() === 1 ? 'response' : 'responses' }} between {{ filter.label() }}
          @if (auth.isAdmin() && !filter.dealerId()) { across all dealers }
        </p>
      </div>

      <section class="card toolbar" aria-label="Refine results">
        <label class="field">
          Would recommend
          <select (change)="recommend.set($any($event.target).value)">
            <option value="">Any answer</option>
            @for (o of recommendOptions; track o[0]) { <option [value]="o[0]">{{ o[1] }}</option> }
          </select>
        </label>
        <label class="field">
          Missing part or defect
          <select (change)="condition.set($any($event.target).value)">
            <option value="">Any answer</option>
            @for (o of conditionOptions; track o[0]) { <option [value]="o[0]">{{ o[1] }}</option> }
          </select>
        </label>
        <label class="field size">
          Rows per page
          <select (change)="pageSize.set(+$any($event.target).value)">
            @for (n of [10, 25, 50, 100]; track n) { <option [value]="n" [selected]="n === pageSize()">{{ n }}</option> }
          </select>
        </label>
      </section>

      <section class="card" [class.stale]="resource.isLoading()">
        @if (resource.error() && !data()) {
          <p class="notice error" role="alert">
            Responses could not be loaded.
            <button class="btn btn-sm" type="button" (click)="resource.reload()">Retry</button>
          </p>
        } @else if (data(); as d) {
          @if (d.items.length === 0) {
            <p class="empty">No survey responses match the selected period and filters.</p>
          } @else {
            <div class="table-wrap">
              <table>
                <caption class="sr-only">Survey responses</caption>
                <thead>
                  <tr>
                    <th scope="col" [attr.aria-sort]="ariaSort('submittedAt')">
                      <button class="sort" type="button" (click)="toggle('submittedAt')">Submitted {{ arrow('submittedAt') }}</button>
                    </th>
                    <th scope="col">Customer</th>
                    <th scope="col">Vehicle</th>
                    @if (auth.isAdmin()) { <th scope="col">Dealer</th> }
                    <th scope="col">Would recommend</th>
                    <th scope="col">Missing part / defect</th>
                    <th scope="col" class="right" [attr.aria-sort]="ariaSort('netScore')">
                      <button class="sort" type="button" (click)="toggle('netScore')">Net score {{ arrow('netScore') }}</button>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  @for (r of d.items; track r.id) {
                    <tr>
                      <td class="num">{{ r.submittedAt | date: 'MMM d, y' }}<br /><span class="muted">{{ r.submittedAt | date: 'shortTime' }}</span></td>
                      <td>{{ r.customerName }}<br /><span class="muted">{{ r.customerEmail }}</span></td>
                      <td>{{ r.vehicle }}</td>
                      @if (auth.isAdmin()) { <td>{{ r.dealerName }}</td> }
                      <td>
                        <span [class]="'chip ' + answerTone(r.recommendScore)">{{ recommendLabel(r.recommend) }}</span>
                        <span class="muted num"> {{ r.recommendScore }}</span>
                      </td>
                      <td>
                        <span [class]="'chip ' + answerTone(r.conditionScore)">{{ conditionLabel(r.vehicleCondition) }}</span>
                        <span class="muted num"> {{ r.conditionScore }}</span>
                      </td>
                      <td class="right num"><span [class]="'chip ' + tone(r.netScore)">{{ r.netScore | number: '1.0-1' }}</span></td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <footer class="pager">
              <span class="muted">Showing {{ from() }}–{{ to() }} of {{ d.total }}</span>
              <span class="buttons">
                <button class="btn btn-sm" type="button" (click)="page.set(page() - 1)" [disabled]="page() <= 1">Previous</button>
                <span class="num">Page {{ page() }} of {{ pages() }}</span>
                <button class="btn btn-sm" type="button" (click)="page.set(page() + 1)" [disabled]="page() >= pages()">Next</button>
              </span>
            </footer>
          }
        } @else {
          <p class="muted" role="status">Loading responses…</p>
        }
      </section>
    </div>
  `,
  styles: `
    .toolbar { display: flex; flex-wrap: wrap; gap: 16px; align-items: end; }
    .toolbar .field { min-width: 210px; }
    .toolbar .size { min-width: 130px; }
    .stale { opacity: 0.65; transition: opacity 0.15s; }
    .sort { all: unset; cursor: pointer; font: inherit; text-transform: inherit; letter-spacing: inherit; }
    .sort:focus-visible { outline: 2px solid var(--focus); outline-offset: 2px; }
    .pager { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: center; gap: 12px; padding-top: 14px; }
    .buttons { display: flex; align-items: center; gap: 12px; }
  `,
})
export class Responses {
  protected readonly auth = inject(AuthService);
  protected readonly filter = inject(FilterService);

  protected readonly recommend = signal('');
  protected readonly condition = signal('');
  protected readonly pageSize = signal(25);
  protected readonly sortBy = signal<SortBy>('submittedAt');
  protected readonly desc = signal(true);

  /** Returns to page 1 whenever the period, dealer or any filter changes. */
  protected readonly page = linkedSignal({
    source: () => [this.filter.params(), this.recommend(), this.condition(), this.pageSize(), this.sortBy(), this.desc()] as const,
    computation: () => 1,
  });

  protected readonly resource = httpResource<Paged<SurveyRow>>(() => {
    const params: Record<string, string | number | boolean> = {
      ...this.filter.params(),
      sortBy: this.sortBy(),
      desc: this.desc(),
      page: this.page(),
      pageSize: this.pageSize(),
    };
    if (this.recommend()) params['recommend'] = this.recommend();
    if (this.condition()) params['condition'] = this.condition();
    return { url: '/api/responses', params };
  });

  protected readonly data = linkedSignal<Paged<SurveyRow> | undefined, Paged<SurveyRow> | undefined>({
    source: () => this.resource.value(),
    computation: (value, previous) => value ?? previous?.value,
  });
  protected readonly total = computed(() => this.data()?.total ?? 0);
  protected readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
  protected readonly from = computed(() => (this.total() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1));
  protected readonly to = computed(() => Math.min(this.total(), this.page() * this.pageSize()));

  protected readonly recommendOptions = Object.entries(RECOMMEND_LABELS);
  protected readonly conditionOptions = Object.entries(CONDITION_LABELS);

  protected recommendLabel = (k: string) => RECOMMEND_LABELS[k] ?? k;
  protected conditionLabel = (k: string) => CONDITION_LABELS[k] ?? k;
  protected tone = (score: number) => toneClass(scoreBand(score).tone);
  protected answerTone = (points: number) => (points >= 100 ? 'tone-ok' : points >= 50 ? 'tone-info' : points >= 25 ? 'tone-warn' : 'tone-bad');

  protected toggle(column: SortBy): void {
    if (this.sortBy() === column) this.desc.update((d) => !d);
    else {
      this.sortBy.set(column);
      this.desc.set(true);
    }
  }

  protected arrow = (column: SortBy) => (this.sortBy() === column ? (this.desc() ? '▼' : '▲') : '');
  protected ariaSort = (column: SortBy) => (this.sortBy() === column ? (this.desc() ? 'descending' : 'ascending') : 'none');
}
