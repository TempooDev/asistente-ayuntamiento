import { EnvironmentProviders, Provider } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '@auth0/auth0-angular';
import { BehaviorSubject, of } from 'rxjs';
import { HubConnectionFactory } from '../app/core/realtime/hub-connection.factory';
import { AUTH_RUNTIME_CONFIG } from '../app/core/auth/auth-runtime-config';

export const TEST_USER = {
  sub: 'auth0|test-user',
  name: 'Test User',
  email: 'test@example.com',
};

/** Minimal Auth0 `AuthService` double: an authenticated user and no-op redirects. */
export class FakeAuthService {
  readonly isLoading$ = of(false);
  readonly isAuthenticated$ = new BehaviorSubject(true);
  readonly user$ = new BehaviorSubject<Record<string, unknown> | null>(TEST_USER);
  readonly idTokenClaims$ = new BehaviorSubject<Record<string, unknown> | null>({ org_id: 'org_test' });
  readonly error$ = of();
  readonly loginCalls: unknown[] = [];
  readonly logoutCalls: unknown[] = [];
  getAccessTokenSilently() {
    return of('test-token');
  }
  loginWithRedirect(options?: unknown) {
    this.loginCalls.push(options);
    return of(undefined);
  }
  logout(options?: unknown) {
    this.logoutCalls.push(options);
    return of(undefined);
  }
}

/** SignalR connection double: handlers are recorded, nothing touches the network. */
export class FakeHubConnection {
  readonly handlers = new Map<string, (...args: unknown[]) => void>();
  on(event: string, handler: (...args: unknown[]) => void) {
    this.handlers.set(event, handler);
  }
  off(event: string) {
    this.handlers.delete(event);
  }
  invoke() {
    return Promise.resolve(undefined);
  }
  send() {
    return Promise.resolve();
  }
  stream() {
    return { subscribe: () => ({ dispose: () => undefined }) };
  }
  start() {
    return Promise.resolve();
  }
  stop() {
    return Promise.resolve();
  }
  onclose() {}
  onreconnecting() {}
  onreconnected() {}
}

/** `HubConnectionFactory` double that hands out fake, always-connected hubs. */
export class FakeHubConnectionFactory {
  readonly connections = new Map<string, FakeHubConnection>();
  get(path: string, configure?: (connection: never) => void) {
    let connection = this.connections.get(path);
    if (!connection) {
      connection = new FakeHubConnection();
      configure?.(connection as never);
      this.connections.set(path, connection);
    }
    return { connection, isConnected: true, ensureConnected: () => Promise.resolve() };
  }
  stopAll() {
    return Promise.resolve();
  }
}

/** Common providers for component and service specs (Auth0, HttpClient testing backend, router, SignalR). */
export function provideTestDependencies(): (Provider | EnvironmentProviders)[] {
  return [
    provideHttpClient(),
    provideHttpClientTesting(),
    provideRouter([]),
    { provide: AuthService, useClass: FakeAuthService },
    { provide: HubConnectionFactory, useClass: FakeHubConnectionFactory },
    { provide: AUTH_RUNTIME_CONFIG, useValue: { domain: 'test.auth0.com', clientId: 'client', audience: 'api', loaded: true } },
  ];
}
