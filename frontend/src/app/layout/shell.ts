import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { FilterService } from '../core/filter.service';
import { Dealer } from '../core/models';
import { DateRangeFilter } from '../shared/date-range-filter';

@Component({
  selector: 'cx-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, DateRangeFilter],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="top">
      <a class="brand" routerLink="/dashboard" aria-label="CX Insight home">
        <span class="mark" aria-hidden="true">CX</span>
        <span class="name">Insight</span>
      </a>
      <nav aria-label="Main">
        <a routerLink="/dashboard" routerLinkActive="active">Dashboard</a>
        <a routerLink="/responses" routerLinkActive="active">Survey responses</a>
        @if (auth.isAdmin()) {
          <a routerLink="/leaderboard" routerLinkActive="active">Leaderboard</a>
        }
      </nav>
      <div class="user">
        <span class="who">
          <strong>{{ auth.user()?.displayName }}</strong>
          <span class="chip">{{ auth.isAdmin() ? 'Administrator' : 'Dealer' }}</span>
        </span>
        <button class="btn btn-sm" type="button" (click)="auth.logout()">Sign out</button>
      </div>
    </header>

    <section class="filters" aria-label="Filters">
      <div class="inner">
        <cx-date-range-filter />
        @if (auth.isAdmin()) {
          <label class="field dealer">
            Dealer
            <select [value]="filter.dealerId() ?? ''" (change)="onDealer($any($event.target).value)">
              <option value="" [selected]="!filter.dealerId()">All dealers</option>
              @for (d of dealers.value() ?? []; track d.id) {
                <option [value]="d.id" [selected]="d.id === filter.dealerId()">{{ d.name }} ({{ d.city }}, {{ d.state }})</option>
              }
            </select>
          </label>
        }
        <p class="period muted">Showing <strong>{{ filter.label() }}</strong></p>
      </div>
    </section>

    <main><router-outlet /></main>
  `,
  styles: `
    .top {
      display: flex; align-items: center; gap: 24px; padding: 0 20px; height: 60px;
      background: var(--surface); border-bottom: 1px solid var(--border);
    }
    .brand { display: flex; align-items: center; gap: 10px; text-decoration: none; color: var(--text); font-weight: 700; }
    .mark { background: var(--brand); color: var(--brand-ink); border-radius: 8px; padding: 3px 8px; font-size: 0.95rem; letter-spacing: 0.02em; }
    nav { display: flex; gap: 4px; flex: 1; overflow-x: auto; }
    nav a { padding: 8px 12px; border-radius: 8px; text-decoration: none; color: var(--muted); font-weight: 600; white-space: nowrap; }
    nav a:hover { background: var(--surface-2); color: var(--text); }
    nav a.active { color: var(--brand); background: var(--brand-soft); }
    .user { display: flex; align-items: center; gap: 12px; }
    .who { display: flex; align-items: center; gap: 8px; }
    .filters { position: sticky; top: 0; z-index: 5; background: var(--surface); border-bottom: 1px solid var(--border); }
    .inner { max-width: 1240px; margin: 0 auto; padding: 12px 16px; display: flex; flex-wrap: wrap; gap: 12px 20px; align-items: end; }
    .dealer { min-width: 260px; }
    .period { margin-left: auto; align-self: center; }
    @media (max-width: 720px) {
      .top { gap: 8px; padding: 0 12px; }
      .name, .who strong { display: none; }
      .period { margin-left: 0; width: 100%; }
    }
  `,
})
export class Shell {
  protected readonly auth = inject(AuthService);
  protected readonly filter = inject(FilterService);
  protected readonly dealers = httpResource<Dealer[]>(() => (this.auth.isAdmin() ? '/api/dealers' : undefined));

  protected onDealer(id: string): void {
    this.filter.dealerId.set(id || null);
  }
}
