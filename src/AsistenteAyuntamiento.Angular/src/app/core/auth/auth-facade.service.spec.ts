import { TestBed } from '@angular/core/testing';
import { HttpBackend } from '@angular/common/http';
import { HttpTestingController } from '@angular/common/http/testing';
import { AuthService } from '@auth0/auth0-angular';
import { firstValueFrom } from 'rxjs';
import { FakeAuthService, FakeHubConnectionFactory, TEST_USER, provideTestDependencies } from '../../../testing/test-providers';
import { HubConnectionFactory } from '../realtime/hub-connection.factory';
import { AuthFacade } from './auth-facade.service';
import { CLAIMS } from './auth-claims';

describe('AuthFacade', () => {
  let auth: FakeAuthService;
  let facade: AuthFacade;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: provideTestDependencies() });
    auth = TestBed.inject(AuthService) as unknown as FakeAuthService;
    facade = TestBed.inject(AuthFacade);
  });

  it('derives roles and isAdmin from the custom claim', () => {
    auth.user$.next({ ...TEST_USER, [CLAIMS.roles]: ['ADMIN-PLATAFORMA', 'Funcionario'] });
    TestBed.tick();

    expect(facade.roles()).toEqual(['admin-plataforma', 'funcionario']);
    expect(facade.isAdmin()).toBe(true);
  });

  it('separates platform and organization administrators', () => {
    auth.user$.next({ ...TEST_USER, [CLAIMS.roles]: ['admin-plataforma'] });
    TestBed.tick();
    expect([facade.isPlatformAdmin(), facade.isOrganizationAdmin(), facade.isAdmin()]).toEqual([true, false, true]);

    auth.user$.next({ ...TEST_USER, [CLAIMS.roles]: ['admin-organizacion'] });
    TestBed.tick();
    expect([facade.isPlatformAdmin(), facade.isOrganizationAdmin(), facade.isAdmin()]).toEqual([false, true, true]);

    auth.user$.next({ ...TEST_USER, [CLAIMS.roles]: ['administrador'] });
    TestBed.tick();
    expect(facade.isAdmin()).toBe(false); // retired role

    auth.user$.next({ ...TEST_USER, [CLAIMS.roles]: ['Funcionario'] });
    TestBed.tick();
    expect(facade.isAdmin()).toBe(false);
  });

  it('logs in with an explicit organization and remembers it', () => {
    facade.loginWithOrg({ organization: 'org_X', invitation: 'inv_1' });

    expect(localStorage.getItem('last_org_id')).toBe('org_X');
    expect(auth.loginCalls[0]).toEqual({ appState: undefined, authorizationParams: { organization: 'org_X', invitation: 'inv_1' } });
  });

  it('stops the realtime hubs before logging out', async () => {
    const hubs = TestBed.inject(HubConnectionFactory) as unknown as FakeHubConnectionFactory;
    let stopped = false;
    hubs.stopAll = () => {
      stopped = true;
      return Promise.resolve();
    };

    await facade.logout();

    expect(stopped).toBe(true);
    expect(auth.logoutCalls).toHaveLength(1);
  });

  it('requests a password reset from Auth0 without the API interceptors', async () => {
    const http = TestBed.inject(HttpTestingController);
    const done = firstValueFrom(facade.requestPasswordReset('ana@example.com'));

    const req = http.expectOne('https://test.auth0.com/dbconnections/change_password');
    expect(req.request.body).toEqual({ client_id: 'client', email: 'ana@example.com', connection: 'Username-Password-Authentication' });
    req.flush('ok');
    await done;
    expect(TestBed.inject(HttpBackend)).toBeTruthy();
  });
});
