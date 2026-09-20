import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL, apiBaseInterceptor, joinApiUrl } from './api-config';

describe('joinApiUrl', () => {
  it('joins without doubling or dropping the slash', () => {
    expect(joinApiUrl('https://api.example.com', '/api/x')).toBe('https://api.example.com/api/x');
    expect(joinApiUrl('https://api.example.com/', '/api/x')).toBe('https://api.example.com/api/x');
  });
});

describe('apiBaseInterceptor', () => {
  function setup(base: string) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiBaseInterceptor])),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: base },
      ],
    });
    return { http: TestBed.inject(HttpClient), ctrl: TestBed.inject(HttpTestingController) };
  }

  it('prefixes relative API calls with the configured base URL', () => {
    const { http, ctrl } = setup('https://api.example.com');
    http.get('/api/dealers').subscribe();
    ctrl.expectOne('https://api.example.com/api/dealers').flush([]);
  });

  it('leaves requests untouched when no base URL is configured (same origin / dev proxy)', () => {
    const { http, ctrl } = setup('');
    http.get('/api/dealers').subscribe();
    ctrl.expectOne('/api/dealers').flush([]);
  });

  it('does not rewrite non-API or already absolute URLs', () => {
    const { http, ctrl } = setup('https://api.example.com');
    http.get('/assets/logo.svg').subscribe();
    http.get('https://other.example.com/api/x').subscribe();
    ctrl.expectOne('/assets/logo.svg').flush('');
    ctrl.expectOne('https://other.example.com/api/x').flush('');
  });
});
