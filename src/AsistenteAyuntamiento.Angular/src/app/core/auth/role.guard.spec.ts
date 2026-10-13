import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { AuthService } from '@auth0/auth0-angular';
import { Observable, firstValueFrom } from 'rxjs';
import { FakeAuthService, TEST_USER, provideTestDependencies } from '../../../testing/test-providers';
import { CLAIMS } from './auth-claims';
import { ROLES } from './roles';
import { roleGuard } from './role.guard';

describe('roleGuard', () => {
  let auth: FakeAuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: provideTestDependencies() });
    auth = TestBed.inject(AuthService) as unknown as FakeAuthService;
  });

  async function run(roles: string[]): Promise<string | boolean> {
    const result = await firstValueFrom(
      TestBed.runInInjectionContext(() =>
        roleGuard({ data: { roles } } as unknown as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
      ) as Observable<boolean | UrlTree>,
    );
    return result instanceof UrlTree ? TestBed.inject(Router).serializeUrl(result) : result;
  }

  it('allows users with the role, ignoring case', async () => {
    auth.user$.next({ ...TEST_USER, [CLAIMS.roles]: ['Administrador'] });
    expect(await run([ROLES.LEGACY_ADMIN])).toBe(true);
  });

  it('accepts a single role given as a string', async () => {
    auth.user$.next({ ...TEST_USER, [CLAIMS.roles]: 'administrador' });
    expect(await run([ROLES.LEGACY_ADMIN])).toBe(true);
  });

  it('sends users without the role to the chat', async () => {
    auth.user$.next({ ...TEST_USER, [CLAIMS.roles]: [ROLES.FUNCIONARIO] });
    expect(await run([ROLES.LEGACY_ADMIN])).toBe('/chat');
  });

  it('sends anonymous users to login', async () => {
    auth.user$.next(null);
    expect(await run([ROLES.LEGACY_ADMIN])).toBe('/login');
  });

  it('allows any signed-in user when the route requires no role', async () => {
    expect(await run([])).toBe(true);
  });
});
