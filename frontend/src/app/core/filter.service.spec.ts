import { TestBed } from '@angular/core/testing';
import { FilterService, presetRange } from './filter.service';
import { AuthService } from './auth.service';

describe('presetRange (must mirror the server PeriodResolver)', () => {
  const today = new Date(2026, 8, 20); // 20 Sep 2026

  it.each([
    ['this_month', '2026-09-01', '2026-09-20'],
    ['last_month', '2026-08-01', '2026-08-31'],
    ['this_quarter', '2026-07-01', '2026-09-20'],
    ['last_quarter', '2026-04-01', '2026-06-30'],
    ['last_30_days', '2026-08-22', '2026-09-20'],
    ['last_6_months', '2026-04-01', '2026-09-20'],
  ] as const)('%s', (id, start, end) => {
    expect(presetRange(id, today)).toEqual({ start, end });
  });

  it('handles the first quarter of the year', () => {
    expect(presetRange('last_quarter', new Date(2026, 1, 10))).toEqual({ start: '2025-10-01', end: '2025-12-31' });
  });
});

describe('FilterService', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [{ provide: AuthService, useValue: { user: () => null } }] });
  });

  it('defaults to the current month', () => {
    const filter = TestBed.inject(FilterService);
    expect(filter.preset()).toBe('this_month');
    expect(filter.range()).toEqual(presetRange('this_month'));
  });

  it('rejects a start date after the end date and keeps the previous range', () => {
    const filter = TestBed.inject(FilterService);
    const before = filter.range();
    expect(filter.setCustom('2026-09-20', '2026-09-01')).toMatch(/must not be after/);
    expect(filter.range()).toEqual(before);
  });

  it('applies a valid custom range and reports it as custom', () => {
    const filter = TestBed.inject(FilterService);
    expect(filter.setCustom('2026-01-01', '2026-01-31')).toBeNull();
    expect(filter.preset()).toBe('custom');
    expect(filter.params()).toEqual({ start: '2026-01-01', end: '2026-01-31' });
  });

  it('adds the dealer to the request parameters only when one is chosen', () => {
    const filter = TestBed.inject(FilterService);
    expect(filter.params()['dealerId']).toBeUndefined();
    filter.dealerId.set('HND-1001');
    expect(filter.params()['dealerId']).toBe('HND-1001');
  });
});
