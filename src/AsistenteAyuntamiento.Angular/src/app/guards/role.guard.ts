import { inject } from '@angular/core';
import { Router, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { AuthService } from '@auth0/auth0-angular';
import { Observable, of } from 'rxjs';
import { map, switchMap, take } from 'rxjs/operators';

export const roleGuardFn = (route: ActivatedRouteSnapshot, state: RouterStateSnapshot): Observable<boolean> => {
  const authService = inject(AuthService);
  const router = inject(Router);

  // Expected roles can be passed in route data, e.g., data: { roles: ['administrador'] }
  const expectedRoles: string[] = route.data['roles'] || [];

  return authService.user$.pipe(
    take(1),
    map(user => {
      if (!user) {
        router.navigate(['/login']);
        return false;
      }

      const rolesClaim = user['https://asistente.ayuntamiento.com/roles'] || [];
      const userRoles: string[] = Array.isArray(rolesClaim) ? rolesClaim : [rolesClaim];

      // If no specific roles required, allow
      if (expectedRoles.length === 0) {
        return true;
      }

      // Check if user has any of the expected roles
      const hasRole = expectedRoles.some(role => userRoles.includes(role));
      
      if (hasRole) {
        return true;
      }

      // If not, redirect to chat (or error page)
      router.navigate(['/chat']);
      return false;
    })
  );
};
