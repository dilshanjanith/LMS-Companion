import { Injectable, signal } from '@angular/core';
import { RuntimeConfiguration } from './models';

const EMPTY_CONFIGURATION: RuntimeConfiguration = {
  apiBaseUrl: 'http://localhost:5150',
  oidc: {
    authority: '',
    clientId: '',
    scope: 'openid profile',
    audience: ''
  },
  canvasInstitutionId: ''
};

@Injectable({ providedIn: 'root' })
export class RuntimeConfigService {
  private readonly configuration = signal<RuntimeConfiguration>(EMPTY_CONFIGURATION);
  readonly loadError = signal<string | null>(null);

  get current(): RuntimeConfiguration {
    return this.configuration();
  }

  async load(): Promise<void> {
    try {
      const response = await fetch('/app-config.json', { cache: 'no-store' });
      if (!response.ok) {
        throw new Error('Configuration response was not successful.');
      }

      const provided = await response.json() as Partial<RuntimeConfiguration>;
      this.configuration.set({
        apiBaseUrl: this.normalizeApiBaseUrl(provided.apiBaseUrl),
        oidc: {
          authority: provided.oidc?.authority?.trim() ?? '',
          clientId: provided.oidc?.clientId?.trim() ?? '',
          scope: provided.oidc?.scope?.trim() || 'openid profile',
          audience: provided.oidc?.audience?.trim() ?? ''
        },
        canvasInstitutionId: provided.canvasInstitutionId?.trim() ?? '',
        allowPersonalTokenTesting: provided.allowPersonalTokenTesting === true
      });
      this.loadError.set(null);
    } catch {
      this.loadError.set('The application configuration could not be loaded. Reload the page or contact your administrator.');
    }
  }

  private normalizeApiBaseUrl(value: string | undefined): string {
    const candidate = value?.trim() || EMPTY_CONFIGURATION.apiBaseUrl;
    try {
      const parsed = new URL(candidate, window.location.origin);
      return parsed.toString().replace(/\/$/, '');
    } catch {
      return EMPTY_CONFIGURATION.apiBaseUrl;
    }
  }
}
