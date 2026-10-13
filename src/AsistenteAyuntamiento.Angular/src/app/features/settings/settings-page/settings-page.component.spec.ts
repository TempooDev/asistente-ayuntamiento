import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '@auth0/auth0-angular';
import { CLAIMS } from '../../../core/auth/auth-claims';
import { FakeAuthService, TEST_USER, provideTestDependencies } from '../../../../testing/test-providers';
import { SettingsPageComponent } from './settings-page.component';

describe('SettingsPageComponent', () => {
  let fixture: ComponentFixture<SettingsPageComponent>;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [SettingsPageComponent],
      providers: provideTestDependencies(),
    }).compileComponents();

    fixture = TestBed.createComponent(SettingsPageComponent);
  });

  it('should create', () => {
    expect(fixture.componentInstance).toBeTruthy();
  });

  function tabsFor(roles: string[]): string[] {
    (TestBed.inject(AuthService) as unknown as FakeAuthService).user$.next({ ...TEST_USER, [CLAIMS.roles]: roles });
    fixture.detectChanges();
    return Array.from(fixture.nativeElement.querySelectorAll('[role="tab"]')).map(t => (t as HTMLElement).textContent!.trim());
  }

  it('shows the AI settings to organization admins and the scraper filters to platform admins', () => {
    expect(tabsFor(['admin-organizacion'])).toEqual(['Configuración de IA']);
    expect(tabsFor(['admin-plataforma'])).toEqual(['Filtros del Scraper']);
    expect(tabsFor(['admin-plataforma', 'admin-organizacion'])).toEqual(['Configuración de IA', 'Filtros del Scraper']);
  });
});
