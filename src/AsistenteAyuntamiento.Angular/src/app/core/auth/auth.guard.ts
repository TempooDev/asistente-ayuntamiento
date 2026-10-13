import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '@auth0/auth0-angular';
import { of, switchMap, take, map } from 'rxjs';
import { AUTH_RUNTIME_CONFIG } from './auth-runtime-config';
import { AuthFacade } from './auth-facade.service';
import { organizationsFromClaims } from './auth-claims';
import { AUTH_ERROR_CODES } from './auth-error-codes';

/**
 * Requires a signed-in user with an organization context. Users without org_id that belong to an organization
 * are silently re-logged into it; users without any organization see an error page.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const facade = inject(AuthFacade);
  const router = inject(Router);
  const errorPage = (code: string) => router.createUrlTree(['/error'], { queryParams: { code } });

  if (!inject(AUTH_RUNTIME_CONFIG).loaded) {
    return of(errorPage(AUTH_ERROR_CODES.configUnavailable));
  }

  return auth.isAuthenticated$.pipe(
    take(1),
    switchMap(isAuthenticated => {
      if (!isAuthenticated) {
        facade.loginWithOrg({ returnTo: state.url });
        return of(false);
      }
      return auth.idTokenClaims$.pipe(
        take(1),
        map(claims => {
          const orgId = claims?.['org_id'] as string | undefined;
          if (orgId) {
            facade.rememberOrganization(orgId);
            return true;
          }
          const organizations = organizationsFromClaims(claims);
          if (organizations.length > 0) {
            // A selector would be needed to choose among several; the first one is used.
            facade.loginWithOrg({ organization: organizations[0], returnTo: state.url, prompt: 'none' });
            return false;
          }
          return errorPage(AUTH_ERROR_CODES.noOrganization);
        }),
      );
    }),
  );
};
