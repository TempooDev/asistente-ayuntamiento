import { EnvironmentProviders, InjectionToken, inject, provideAppInitializer } from '@angular/core';
import { HttpBackend, HttpClient } from '@angular/common/http';
import { AuthClientConfig } from '@auth0/auth0-angular';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

/** Auth0 settings served by the API at /api/config/auth0 (they differ per deployment). */
export interface AuthRuntimeConfig {
  domain: string;
  clientId: string;
  audience: string;
  /** False until the settings were loaded; the auth guard sends the user to /error if loading failed. */
  loaded: boolean;
}

export const AUTH_RUNTIME_CONFIG = new InjectionToken<AuthRuntimeConfig>('AUTH_RUNTIME_CONFIG', {
  providedIn: 'root',
  factory: () => ({ domain: '', clientId: '', audience: '', loaded: false }),
});

/**
 * Loads the Auth0 settings before the app starts and configures the Auth0 SDK. HttpBackend bypasses the
 * interceptors (the auth interceptor needs this very configuration). If loading fails, the SDK still gets a
 * placeholder configuration so the app can start and show the error page.
 */
export function provideAuthRuntimeConfig(): EnvironmentProviders {
  return provideAppInitializer(async () => {
    const runtime = inject(AUTH_RUNTIME_CONFIG);
    const sdkConfig = inject(AuthClientConfig);
    const http = new HttpClient(inject(HttpBackend));

    try {
      const loaded = await firstValueFrom(
        http.get<Partial<AuthRuntimeConfig>>(`${environment.apiBaseUrl}/api/config/auth0`),
      );
      runtime.domain = loaded.domain ?? '';
      runtime.clientId = loaded.clientId ?? '';
      runtime.audience = loaded.audience ?? '';
      runtime.loaded = !!runtime.domain && !!runtime.clientId;
    } catch (err) {
      console.error('Failed to load the Auth0 configuration', err);
    }

    sdkConfig.set({
      domain: runtime.domain || 'auth-config-unavailable.invalid',
      clientId: runtime.clientId || 'unavailable',
      authorizationParams: {
        redirect_uri: `${window.location.origin}/callback`,
        audience: runtime.audience,
      },
      cacheLocation: 'localstorage',
      useRefreshTokens: true,
      errorPath: '/error',
      httpInterceptor: {
        allowedList: ['/api/*', '/hubs/*', `${environment.apiBaseUrl}/api/*`, `${environment.apiBaseUrl}/hubs/*`],
      },
    });
  });
}
