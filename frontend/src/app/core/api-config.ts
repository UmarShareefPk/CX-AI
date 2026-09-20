import { HttpInterceptorFn } from '@angular/common/http';
import { InjectionToken, inject } from '@angular/core';
import { environment } from '../../environments/environment';

/** Base URL of the API, from the environment file (empty means same origin). Override in tests or per-deployment via DI. */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => environment.apiBaseUrl,
});

export function joinApiUrl(base: string, path: string): string {
  return base.replace(/\/+$/, '') + path;
}

/**
 * Call sites use relative '/api/...' paths; this makes them absolute when a base URL is configured.
 * It must run after the auth interceptor, which recognises API requests by their relative '/api' prefix.
 */
export const apiBaseInterceptor: HttpInterceptorFn = (req, next) => {
  const base = inject(API_BASE_URL);
  return next(base && req.url.startsWith('/api') ? req.clone({ url: joinApiUrl(base, req.url) }) : req);
};
