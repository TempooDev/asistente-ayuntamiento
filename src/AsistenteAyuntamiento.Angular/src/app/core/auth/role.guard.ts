import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '@auth0/auth0-angular';
import { map, take } from 'rxjs';
import { AuthFacade } from './auth-facade.service';
import { rolesFromClaims } from './auth-claims';

/**
 * Requires one of route.data.roles. Only improves the UX: the API authorizes every admin call itself.
 * Users without the role go to the chat; anonymous users to the login page.
 */
export const roleGuard: CanActivateFn = route => {
  const auth = inject(AuthService);
  const facade = inject(AuthFacade);
  const router = inject(Router);
  const required: string[] = route.data['roles'] ?? [];

  return auth.user$.pipe(
    take(1),
    map(user => {
      if (!user) return router.createUrlTree(['/login']);
      return facade.hasAnyRole(required, rolesFromClaims(user)) ? true : router.createUrlTree(['/chat']);
    }),
  );
};
