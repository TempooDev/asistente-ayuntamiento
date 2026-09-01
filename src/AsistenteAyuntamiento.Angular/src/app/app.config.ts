import { ApplicationConfig, provideBrowserGlobalErrorListeners, APP_INITIALIZER } from '@angular/core';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';

import { provideHttpClient, withInterceptors, HttpClient } from '@angular/common/http';
import { provideAuth0, authHttpInterceptorFn, AuthClientConfig } from '@auth0/auth0-angular';
import { environment } from '../environments/environment';
import { firstValueFrom } from 'rxjs';

import { HttpBackend } from '@angular/common/http';

function auth0ConfigFactory(handler: HttpBackend, config: AuthClientConfig) {
  return () => {
    const http = new HttpClient(handler);
    return firstValueFrom(http.get<any>('/api/config/auth0')).then((loadedConfig) => {
      // Update environment directly for other components that might read it (e.g. perfil.ts)
      environment.auth0.domain = loadedConfig.domain || '';
      environment.auth0.clientId = loadedConfig.clientId || '';
      environment.auth0.audience = loadedConfig.audience || '';

      config.set({
        domain: loadedConfig.domain || '',
        clientId: loadedConfig.clientId || '',
        authorizationParams: {
          redirect_uri: window.location.origin + '/callback',
          audience: loadedConfig.audience || ''
        },
        cacheLocation: 'localstorage',
        useRefreshTokens: true,
        httpInterceptor: {
          allowedList: [
            '/api/*',
            '/hubs/*',
            `${environment.apiBaseUrl}/api/*`,
            `${environment.apiBaseUrl}/hubs/*`
          ]
        }
      });
    }).catch((err) => {
      console.error('Failed to load Auth0 config:', err);
    });
  };
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(
      withInterceptors([authHttpInterceptorFn])
    ),
    provideAuth0(),
    {
      provide: APP_INITIALIZER,
      useFactory: auth0ConfigFactory,
      deps: [HttpBackend, AuthClientConfig],
      multi: true
    }
  ]
};
