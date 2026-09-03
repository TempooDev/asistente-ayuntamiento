import { inject } from '@angular/core';
import { Router, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { AuthService } from '@auth0/auth0-angular';
import { Observable, of } from 'rxjs';
import { map, switchMap, tap } from 'rxjs/operators';

export const customAuthGuardFn = (route: ActivatedRouteSnapshot, state: RouterStateSnapshot): Observable<boolean> => {
  const authService = inject(AuthService);
  const router = inject(Router);

  return authService.isAuthenticated$.pipe(
    switchMap(isAuthenticated => {
      if (!isAuthenticated) {
        // Usuario no autenticado: intentamos usar la última organización guardada
        const lastOrgId = localStorage.getItem('last_org_id');
        authService.loginWithRedirect({
          appState: { target: state.url },
          authorizationParams: lastOrgId ? { organization: lastOrgId } : undefined
        });
        return of(false);
      }

      // Usuario autenticado: verificamos si tiene contexto de organización
      return authService.idTokenClaims$.pipe(
        map(claims => {
          if (!claims) return false;

          // Si ya tiene org_id, todo está perfecto, guardamos el último y dejamos pasar
          if (claims['org_id']) {
            localStorage.setItem('last_org_id', claims['org_id']);
            return true;
          }

          // Si NO tiene org_id, buscamos el claim personalizado que inyectaremos con la Action de Auth0
          const orgsClaim = claims['https://asistente.ayuntamiento.com/orgs'];
          
          if (orgsClaim && Array.isArray(orgsClaim) && orgsClaim.length > 0) {
            // Si pertenece a 1 o más organizaciones, hacemos re-login automático a la primera
            // (Para soportar múltiples, aquí redirigiríamos a una pantalla de selector)
            const targetOrg = orgsClaim[0].id || orgsClaim[0]; // Soporta array de IDs o array de objetos
            authService.loginWithRedirect({
              appState: { target: state.url },
              authorizationParams: { organization: targetOrg, prompt: 'none' } // prompt: none para que sea silente
            });
            return false;
          }

          // Si no tiene organizaciones ni contexto, no puede usar la app.
          router.navigate(['/error'], { 
            queryParams: { 
              message: 'No perteneces a ninguna organización. Solicita una invitación al administrador.' 
            }
          });
          return false;
        })
      );
    })
  );
};
