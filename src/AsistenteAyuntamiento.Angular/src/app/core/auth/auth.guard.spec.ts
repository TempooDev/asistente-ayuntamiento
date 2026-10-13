import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { AuthService } from '@auth0/auth0-angular';
import { Observable, firstValueFrom, isObservable } from 'rxjs';
import { FakeAuthService, provideTestDependencies } from '../../../testing/test-providers';
import { AUTH_RUNTIME_CONFIG } from './auth-runtime-config';
import { CLAIMS } from './auth-claims';
import { authGuard } from './auth.guard';

describe('authGuard', () => {
  let auth: FakeAuthService;

  function setup(configLoaded = true) {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideTestDependencies(),
        { provide: AUTH_RUNTIME_CONFIG, useValue: { domain: 'd', clientId: 'c', audience: 'a', loaded: configLoaded } },
      ],
    });
    auth = TestBed.inject(AuthService) as unknown as FakeAuthService;
  }

  async function run(): Promise<boolean | UrlTree> {
    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url: '/documentos' } as RouterStateSnapshot),
    ) as Observable<boolean | UrlTree> | boolean | UrlTree;
    return isObservable(result) ? firstValueFrom(result) : result;
  }

  const serialized = (tree: boolean | UrlTree) => (tree instanceof UrlTree ? TestBed.inject(Router).serializeUrl(tree) : tree);

  it('lets users with an organization in and remembers it', async () => {
    setup();
    auth.idTokenClaims$.next({ org_id: 'org_A' });

    expect(await run()).toBe(true);
    expect(localStorage.getItem('last_org_id')).toBe('org_A');
  });

  it('sends anonymous users to login with the last organization and the target url', async () => {
    setup();
    localStorage.setItem('last_org_id', 'org_A');
    auth.isAuthenticated$.next(false);

    expect(await run()).toBe(false);
    expect(auth.loginCalls[0]).toEqual({
      appState: { target: '/documentos' },
      authorizationParams: { organization: 'org_A' },
    });
  });

  it('silently switches users without org_id into their first organization', async () => {
    setup();
    auth.idTokenClaims$.next({ [CLAIMS.organizations]: [{ id: 'org_B' }, 'org_C'] });

    expect(await run()).toBe(false);
    expect(auth.loginCalls[0]).toEqual({
      appState: { target: '/documentos' },
      authorizationParams: { organization: 'org_B', prompt: 'none' },
    });
  });

  it('shows an error code to users without any organization', async () => {
    setup();
    auth.idTokenClaims$.next({});

    expect(serialized(await run())).toBe('/error?code=NO_ORGANIZATION');
  });

  it('shows an error code when the Auth0 configuration could not be loaded', async () => {
    setup(false);

    expect(serialized(await run())).toBe('/error?code=AUTH_CONFIG_UNAVAILABLE');
    expect(auth.loginCalls).toHaveLength(0);
  });
});

