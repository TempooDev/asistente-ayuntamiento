import { Injectable, computed, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { HttpBackend, HttpClient } from '@angular/common/http';
import { toSignal } from '@angular/core/rxjs-interop';
import { AuthService, RedirectLoginOptions } from '@auth0/auth0-angular';
import { Observable, map } from 'rxjs';
import { HubConnectionFactory } from '../realtime/hub-connection.factory';
import { AUTH_RUNTIME_CONFIG } from './auth-runtime-config';
import { rolesFromClaims } from './auth-claims';
import { ROLES } from './roles';

const LAST_ORGANIZATION_KEY = 'last_org_id';

export interface LoginOptions {
  /** Auth0 organization; defaults to the last one used on this device. */
  organization?: string;
  invitation?: string;
  /** Route to return to after login. */
  returnTo?: string;
  /** 'none' for a silent organization switch, 'login' to force the credentials screen. */
  prompt?: 'none' | 'login';
}

/** Single entry point for the signed-in user: state, roles, organization, login and logout. */
@Injectable({ providedIn: 'root' })
export class AuthFacade {
  private readonly auth = inject(AuthService);
  private readonly document = inject(DOCUMENT);
  private readonly hubs = inject(HubConnectionFactory);
  private readonly runtimeConfig = inject(AUTH_RUNTIME_CONFIG);
  /** Without interceptors: the Auth0 endpoints must not receive the API token. */
  private readonly plainHttp = new HttpClient(inject(HttpBackend));

  readonly isLoading = toSignal(this.auth.isLoading$, { initialValue: true });
  readonly isAuthenticated = toSignal(this.auth.isAuthenticated$, { initialValue: false });
  readonly user = toSignal(this.auth.user$, { initialValue: null });
  readonly roles = computed(() => rolesFromClaims(this.user()));
  readonly isAdmin = computed(() => this.roles().includes(ROLES.ADMIN.toLowerCase()));
  readonly orgId = computed(() => (this.user()?.['org_id'] as string | undefined) ?? this.lastOrganization());

  /** Whether the user has at least one of roles (case-insensitive). No roles required means true. */
  hasAnyRole(roles: readonly string[], userRoles = this.roles()): boolean {
    return roles.length === 0 || roles.some(role => userRoles.includes(role.toLowerCase()));
  }

  loginWithOrg(options: LoginOptions = {}): void {
    const organization = options.organization ?? this.lastOrganization() ?? undefined;
    if (options.organization) {
      this.rememberOrganization(options.organization);
    }
    const authorizationParams: NonNullable<RedirectLoginOptions<unknown>['authorizationParams']> = {};
    if (organization) authorizationParams.organization = organization;
    if (options.invitation) authorizationParams.invitation = options.invitation;
    if (options.prompt) authorizationParams.prompt = options.prompt;

    this.auth.loginWithRedirect({
      appState: options.returnTo ? { target: options.returnTo } : undefined,
      authorizationParams: Object.keys(authorizationParams).length > 0 ? authorizationParams : undefined,
    });
  }

  /** Closes the SignalR connections first so no hub keeps running with the old session. */
  async logout(): Promise<void> {
    await this.hubs.stopAll();
    this.auth.logout({ logoutParams: { returnTo: this.document.location.origin } });
  }

  rememberOrganization(organization: string): void {
    try {
      localStorage.setItem(LAST_ORGANIZATION_KEY, organization);
    } catch {
      // Storage unavailable (private mode): the organization is simply not remembered.
    }
  }

  lastOrganization(): string | null {
    try {
      return localStorage.getItem(LAST_ORGANIZATION_KEY);
    } catch {
      return null;
    }
  }

  /** Asks Auth0 to email a password-reset link (database connections only; social logins have no password). */
  requestPasswordReset(email: string): Observable<void> {
    return this.plainHttp
      .post(
        `https://${this.runtimeConfig.domain}/dbconnections/change_password`,
        { client_id: this.runtimeConfig.clientId, email, connection: 'Username-Password-Authentication' },
        { responseType: 'text' },
      )
      .pipe(map(() => undefined));
  }
}
