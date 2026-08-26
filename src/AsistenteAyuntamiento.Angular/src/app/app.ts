import { Component, inject } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '@auth0/auth0-angular';
import { AsyncPipe, NgIf } from '@angular/common';
import { DOCUMENT } from '@angular/common';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, AsyncPipe, NgIf],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  public auth = inject(AuthService);
  private document = inject(DOCUMENT);

  login() {
    this.auth.loginWithRedirect();
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
