import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthFacade } from './core/auth/auth-facade.service';
import { ThemeService } from './core/layout/theme.service';
import { ConfirmDialogComponent } from './shared/ui/confirm-dialog.component';
import { ToastContainerComponent } from './shared/ui/toast-container.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ToastContainerComponent, ConfirmDialogComponent],
  templateUrl: './app.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppComponent {
  protected readonly auth = inject(AuthFacade);
  protected readonly theme = inject(ThemeService);
  protected readonly defaultAvatar = '/default-avatar.svg';

  constructor() {
    this.theme.restore();
  }

  login(): void {
    this.auth.loginWithOrg();
  }

  logout(): Promise<void> {
    return this.auth.logout();
  }
}
