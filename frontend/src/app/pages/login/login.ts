import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'cx-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="wrap">
      <section class="card panel" aria-labelledby="title">
        <div class="logo" aria-hidden="true"><span>CX</span></div>
        <h1 id="title">CX Insight</h1>
        <p class="muted lead">Customer satisfaction for your dealership. Sign in to see your survey results.</p>

        <form (submit)="submit($event)">
          <label class="field">
            Username
            <input type="text" name="username" autocomplete="username" required
                   [value]="username()" (input)="username.set($any($event.target).value)" />
          </label>
          <label class="field">
            Password
            <input type="password" name="password" autocomplete="current-password" required
                   [value]="password()" (input)="password.set($any($event.target).value)" />
          </label>
          @if (error()) {
            <p class="notice error" role="alert">{{ error() }}</p>
          }
          <button class="btn btn-primary" type="submit" [disabled]="busy() || !username() || !password()">
            {{ busy() ? 'Signing in…' : 'Sign in' }}
          </button>
        </form>
      </section>
    </main>
  `,
  styles: `
    .wrap { min-height: 100vh; display: grid; place-items: center; padding: 24px 16px; }
    .panel { width: min(420px, 100%); padding: 32px; display: grid; gap: 12px; }
    .logo span { display: inline-block; background: var(--brand); color: var(--brand-ink); font-weight: 800; padding: 6px 12px; border-radius: 10px; }
    .lead { margin-bottom: 8px; }
    form { display: grid; gap: 14px; }
  `,
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly returnUrl = input<string>(); // bound from ?returnUrl=
  protected readonly username = signal('');
  protected readonly password = signal('');
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.login(this.username().trim(), this.password());
      await this.router.navigateByUrl(this.returnUrl() || '/dashboard');
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
