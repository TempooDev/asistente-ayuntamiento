import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthService } from '@auth0/auth0-angular';
import { FakeAuthService, TEST_USER, provideTestDependencies } from '../testing/test-providers';
import { AppComponent } from './app.component';
import { CLAIMS } from './core/auth/auth-claims';
import { InactivityService } from './core/auth/inactivity.service';

describe('AppComponent', () => {
  let auth: FakeAuthService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [...provideTestDependencies(), { provide: InactivityService, useValue: { start: vi.fn(), stop: vi.fn() } }],
    }).compileComponents();
    auth = TestBed.inject(AuthService) as unknown as FakeAuthService;
  });

  function render() {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const menuButton = () => el.querySelector<HTMLButtonElement>('button[aria-label="Abrir menú"]');
    const menu = () => el.querySelector<HTMLElement>('#mobile-menu');
    return { fixture, el, menuButton, menu };
  }

  it('should create the app', () => {
    expect(render().fixture.componentInstance).toBeTruthy();
  });

  it('starts the inactivity timer only while authenticated', () => {
    const inactivity = TestBed.inject(InactivityService) as unknown as { start: ReturnType<typeof vi.fn>; stop: ReturnType<typeof vi.fn> };
    const { fixture } = render();
    expect(inactivity.start).toHaveBeenCalled();

    auth.isAuthenticated$.next(false);
    fixture.detectChanges();
    expect(inactivity.stop).toHaveBeenCalled();
  });

  it('opens the mobile menu from the header and closes it with Escape or the backdrop', () => {
    const { fixture, el, menuButton, menu } = render();
    expect(menu()).toBeNull();
    expect(menuButton()!.getAttribute('aria-expanded')).toBe('false');

    menuButton()!.click();
    fixture.detectChanges();
    expect(menu()!.getAttribute('role')).toBe('dialog');
    expect(menuButton()!.getAttribute('aria-expanded')).toBe('true');

    menu()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(menu()).toBeNull();

    menuButton()!.click();
    fixture.detectChanges();
    el.querySelector<HTMLElement>('[data-testid="mobile-menu-backdrop"]')!.click();
    fixture.detectChanges();
    expect(menu()).toBeNull();
  });

  it('closes the mobile menu after navigating', async () => {
    const { fixture, menuButton, menu } = render();
    menuButton()!.click();
    fixture.detectChanges();

    await TestBed.inject(Router).navigateByUrl('/');
    fixture.detectChanges();

    expect(menu()).toBeNull();
  });

  it('shows the admin links only to administrators, in both menus', () => {
    const labels = (root: Element | null) => Array.from(root?.querySelectorAll('a') ?? []).map(a => a.querySelector('span:last-child')!.textContent!.trim());

    const regular = render();
    regular.menuButton()!.click();
    regular.fixture.detectChanges();
    expect(labels(regular.menu())).toEqual(['Chat', 'Perfil']);

    auth.user$.next({ ...TEST_USER, [CLAIMS.roles]: ['administrador'] });
    regular.fixture.detectChanges();
    expect(labels(regular.menu())).toContain('Documentos');
    expect(labels(regular.el.querySelector('aside'))).toContain('Métricas');
  });

  it('shows each admin scope only its own pages', () => {
    const { fixture, el } = render();
    const links = () => Array.from(el.querySelector('aside')!.querySelectorAll('a')).map(a => a.querySelector('span:last-child')!.textContent!.trim());

    auth.user$.next({ ...TEST_USER, [CLAIMS.roles]: ['admin-plataforma'] });
    fixture.detectChanges();
    expect(links()).toContain('Documentos');
    expect(links()).not.toContain('Arena (A/B)');

    auth.user$.next({ ...TEST_USER, [CLAIMS.roles]: ['admin-organizacion'] });
    fixture.detectChanges();
    expect(links()).toContain('Arena (A/B)');
    expect(links()).not.toContain('Documentos');
    expect(links()).toContain('Configuración');
  });
});
