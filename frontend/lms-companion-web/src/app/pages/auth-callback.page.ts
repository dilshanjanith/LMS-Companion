import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';

@Component({
  standalone: true,
  imports: [RouterLink],
  template: `
    <main class="callback-page">
      <section class="callback-card" aria-live="polite">
        @if (auth.callbackError() || !auth.isConfigured()) {
          <span class="callback-symbol callback-symbol-error" aria-hidden="true">!</span>
          <h1>Sign-in couldn’t be completed</h1>
          <p>Your account could not be verified. Please start again or contact your administrator.</p>
          @if (auth.callbackDiagnostic()) {
            <p class="diagnostic">Diagnostic code: <code>{{ auth.callbackDiagnostic() }}</code></p>
          }
          <a routerLink="/">Return to sign in</a>
        } @else {
          <span class="callback-spinner" aria-hidden="true"></span>
          <h1>Finishing secure sign-in</h1>
          <p>You’ll be taken to your learning dashboard in a moment.</p>
        }
      </section>
    </main>
  `,
  styles: [`
    :host { display: block; min-height: 100vh; }
    .callback-page { display: grid; place-items: center; min-height: 100vh; padding: 24px; }
    .callback-card { max-width: 440px; padding: 38px; border: 1px solid #e4eae5; border-radius: 22px; background: #fff; box-shadow: var(--shadow); text-align: center; }
    h1 { margin: 20px 0 10px; color: var(--ink); font: 500 30px/1.15 Georgia, serif; }
    p { margin: 0 0 22px; color: var(--muted); line-height: 1.6; }
    .diagnostic { margin-top: -10px; font-size: 13px; }
    code { font-family: ui-monospace, monospace; }
    a { color: var(--green); font-weight: 700; }
    .callback-spinner, .callback-symbol { display: inline-grid; place-items: center; width: 44px; height: 44px; border-radius: 50%; }
    .callback-spinner { border: 3px solid #dcece2; border-top-color: var(--green); animation: spin .8s linear infinite; }
    .callback-symbol-error { background: var(--red-soft); color: var(--red); font-size: 22px; font-weight: 800; }
    @keyframes spin { to { transform: rotate(360deg); } }
  `]
})
export class AuthCallbackPage {
  readonly auth = inject(AuthService);
}
