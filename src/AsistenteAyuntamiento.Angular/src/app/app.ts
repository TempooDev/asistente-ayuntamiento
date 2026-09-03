import { environment } from "../environments/environment";

import { Component, inject } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '@auth0/auth0-angular';
import { DOCUMENT, AsyncPipe } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, AsyncPipe],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  public auth = inject(AuthService);
  private document = inject(DOCUMENT);

  // Using signals for reactive state
  isAuthenticated = toSignal(this.auth.isAuthenticated$, { initialValue: false });
  user = toSignal(this.auth.user$);

  isAdmin = () => {
    const userData = this.user();
    if (!userData) return false;
    const rolesClaim = userData[`${environment.auth0.customClaimsNamespace}/roles`] || [];
    const roles = Array.isArray(rolesClaim) ? rolesClaim : [rolesClaim];
    return roles.includes('administrador');
  };

  login() {
    const lastOrgId = localStorage.getItem('last_org_id');
    this.auth.loginWithRedirect({
      authorizationParams: lastOrgId ? { organization: lastOrgId } : undefined
    });
  }

  logout() {
    this.auth.logout({ logoutParams: { returnTo: this.document.location.origin } });
  }

  toggleTheme() {
    const root = this.document.documentElement;
    const isDark = root.classList.contains('dark') || root.getAttribute('data-theme') === 'dark';
    const next = isDark ? 'light' : 'dark';
    root.setAttribute('data-theme', next);
    if (next === 'dark') {
        root.classList.add('dark');
    } else {
        root.classList.remove('dark');
    }
    localStorage.setItem('theme', next);
  }
}
