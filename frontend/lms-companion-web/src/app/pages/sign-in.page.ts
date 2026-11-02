import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { RuntimeConfigService } from '../core/runtime-config.service';

@Component({
  standalone: true,
  selector: 'app-sign-in-page',
  templateUrl: './sign-in.page.html'
})
export class SignInPage {
  readonly auth = inject(AuthService);
  readonly config = inject(RuntimeConfigService);
  readonly router = inject(Router);
  readonly signingIn = signal(false);
  readonly error = signal<string | null>(null);
  readonly configurationReady = computed(() => this.auth.isConfigured() && !this.config.loadError());

  constructor() {
    if (this.auth.isAuthenticated()) {
      void this.router.navigateByUrl('/dashboard');
    }
  }

  async signIn(): Promise<void> {
    this.error.set(null);
    this.signingIn.set(true);
    try {
      await this.auth.signIn();
    } catch {
      this.error.set('Sign-in could not be started. Please try again or contact your administrator.');
      this.signingIn.set(false);
    }
  }
}
