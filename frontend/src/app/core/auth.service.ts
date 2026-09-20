import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { LoginResponse, User } from './models';

const STORAGE_KEY = 'cx.session';

interface Session {
  token: string;
  expiresAt: string;
  user: User;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly session = signal<Session | null>(this.restore());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly token = computed(() => this.session()?.token ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null);
  readonly isAdmin = computed(() => this.user()?.role === 'Admin');

  async login(username: string, password: string): Promise<void> {
    try {
      const res = await firstValueFrom(this.http.post<LoginResponse>('/api/auth/login', { username, password }));
      const session: Session = { token: res.token, expiresAt: res.expiresAt, user: res.user };
      this.session.set(session);
      this.persist(session);
    } catch (e) {
      throw new Error(problemMessage(e, 'Sign in failed. Please try again.'));
    }
  }

  logout(redirect = true): void {
    this.session.set(null);
    try {
      sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      /* storage unavailable: nothing to clear */
    }
    if (redirect) void this.router.navigate(['/login']);
  }

  // sessionStorage (not localStorage): the session ends when the tab closes.
  private persist(session: Session): void {
    try {
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    } catch {
      /* private mode: the session simply lasts until reload */
    }
  }

  private restore(): Session | null {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const session = JSON.parse(raw) as Session;
      return new Date(session.expiresAt).getTime() > Date.now() ? session : null;
    } catch {
      return null;
    }
  }
}

/** Extracts the RFC 9457 `detail` from an HTTP error, or falls back to a friendly message. */
export function problemMessage(error: unknown, fallback: string): string {
  const e = error as { status?: number; error?: { detail?: string; title?: string } };
  if (e?.status === 0) return 'Cannot reach the server. Is the API running?';
  if (e?.status === 429) return 'Too many requests. Please wait a moment and try again.';
  return e?.error?.detail ?? e?.error?.title ?? fallback;
}
