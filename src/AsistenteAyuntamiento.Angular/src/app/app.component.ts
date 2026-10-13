import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthFacade } from './core/auth/auth-facade.service';
import { InactivityService } from './core/auth/inactivity.service';
import { ThemeService } from './core/layout/theme.service';
import { ConfirmDialogComponent } from './shared/ui/confirm-dialog.component';
import { DialogFocusDirective } from './shared/ui/dialog-focus.directive';
import { ToastContainerComponent } from './shared/ui/toast-container.component';

interface NavItem {
  path: string;
  label: string;
  icon: string;
  adminOnly?: boolean;
}

const NAV_ITEMS: readonly NavItem[] = [
  { path: '/chat', label: 'Chat', icon: 'chat' },
  { path: '/documentos', label: 'Documentos', icon: 'description', adminOnly: true },
  { path: '/configuracion', label: 'Configuración IA', icon: 'settings_input_component', adminOnly: true },
  { path: '/arena', label: 'Arena (A/B)', icon: 'compare_arrows', adminOnly: true },
  { path: '/admin/analytics', label: 'Métricas', icon: 'bar_chart', adminOnly: true },
  { path: '/perfil', label: 'Perfil', icon: 'account_circle' },
];

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ToastContainerComponent, ConfirmDialogComponent, DialogFocusDirective],
  templateUrl: './app.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppComponent {
  protected readonly auth = inject(AuthFacade);
  protected readonly theme = inject(ThemeService);
  private readonly inactivity = inject(InactivityService);
  protected readonly defaultAvatar = '/default-avatar.svg';

  /** Links shown in both the desktop sidebar and the mobile menu, filtered by role once. */
  protected readonly navItems = computed(() => NAV_ITEMS.filter(item => !item.adminOnly || this.auth.isAdmin()));
  protected readonly mobileMenuOpen = signal(false);

  constructor() {
    this.theme.restore();

    effect(() => {
      if (this.auth.isAuthenticated()) {
        this.inactivity.start();
      } else {
        this.inactivity.stop();
      }
    });

    inject(Router)
      .events.pipe(
        filter(event => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.closeMobileMenu());
  }

  toggleMobileMenu(): void {
    this.mobileMenuOpen.update(open => !open);
  }

  closeMobileMenu(): void {
    this.mobileMenuOpen.set(false);
  }

  login(): void {
    this.auth.loginWithOrg();
  }

  logout(): Promise<void> {
    this.closeMobileMenu();
    return this.auth.logout();
  }
}
