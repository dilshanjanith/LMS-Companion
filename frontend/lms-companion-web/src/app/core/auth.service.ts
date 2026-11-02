import { computed, Injectable, signal } from '@angular/core';
import { ErrorResponse, User, UserManager, WebStorageStateStore } from 'oidc-client-ts';
import { RuntimeConfigService } from './runtime-config.service';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private manager: UserManager | null = null;
  readonly user = signal<User | null>(null);
  readonly callbackError = signal(false);
  readonly callbackDiagnostic = signal<string | null>(null);
  readonly isAuthenticated = computed(() => {
    const current = this.user();
    return !!current?.access_token && !current.expired;
  });
  readonly isConfigured = computed(() => {
    const oidc = this.runtimeConfig.current.oidc;
    if (!oidc.clientId || !oidc.authority || !oidc.audience) return false;
    try {
      const authority = new URL(oidc.authority);
      return authority.protocol === 'https:' && !authority.hostname.endsWith('.invalid');
    } catch {
      return false;
    }
  });
  readonly displayName = computed(() => {
    const profile = this.user()?.profile;
    const name = profile?.['name'] ?? profile?.['preferred_username'] ?? profile?.['email'];
    return typeof name === 'string' && name.trim() ? name : 'Student';
  });

  constructor(private readonly runtimeConfig: RuntimeConfigService) {}

  async initialize(): Promise<void> {
    if (!this.isConfigured()) return;

    const { authority, clientId, scope, audience } = this.runtimeConfig.current.oidc;
    const redirectUri = new URL('/auth/callback', window.location.origin).toString();
    this.manager = new UserManager({
      authority,
      client_id: clientId,
      redirect_uri: redirectUri,
      post_logout_redirect_uri: new URL('/', window.location.origin).toString(),
      response_type: 'code',
      scope,
      extraQueryParams: { audience },
      loadUserInfo: false,
      automaticSilentRenew: false,
      userStore: new WebStorageStateStore({ store: window.sessionStorage }),
      stateStore: new WebStorageStateStore({ store: window.sessionStorage })
    });

    if (window.location.pathname === '/auth/callback') {
      try {
        const callbackUser = await this.manager.signinCallback();
        if (!callbackUser?.access_token || callbackUser.expired) {
          throw new Error('No valid user session was returned.');
        }

        this.user.set(callbackUser);
        window.history.replaceState({}, document.title, '/dashboard');
      } catch (error) {
        this.callbackDiagnostic.set(getSafeCallbackDiagnostic(error));
        this.callbackError.set(true);
        await this.manager.removeUser();
        window.history.replaceState({}, document.title, '/auth/callback');
      }
      return;
    }

    const current = await this.manager.getUser();
    if (current?.expired) {
      await this.manager.removeUser();
      this.user.set(null);
    } else {
      this.user.set(current);
    }
  }

  async signIn(): Promise<void> {
    if (!this.manager) throw new Error('Sign-in is not configured.');
    await this.manager.signinRedirect();
  }

  async signOut(): Promise<void> {
    if (!this.manager) {
      this.user.set(null);
      return;
    }

    try {
      await this.manager.signoutRedirect();
    } catch {
      await this.manager.removeUser();
      this.user.set(null);
    }
  }

  getAccessToken(): string | null {
    const current = this.user();
    return current && !current.expired ? current.access_token : null;
  }
}

function getSafeCallbackDiagnostic(error: unknown): string {
  if (error instanceof ErrorResponse) {
    const code = error.error;
    return typeof code === 'string' && /^[a-z0-9_-]{1,64}$/i.test(code)
      ? `provider_${code.toLowerCase()}`
      : 'provider_callback_error';
  }

  const message = error instanceof Error ? error.message.toLowerCase() : '';
  if (message.includes('state')) return 'state_validation_failed';
  if (message.includes('nonce')) return 'nonce_validation_failed';
  if (message.includes('issuer')) return 'issuer_validation_failed';
  if (message.includes('audience')) return 'audience_validation_failed';
  if (message.includes('signature')) return 'signature_validation_failed';
  if (message.includes('fetch') || message.includes('network')) return 'identity_provider_network_error';
  return 'callback_processing_failed';
}
