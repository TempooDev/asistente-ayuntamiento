import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuthService } from '@auth0/auth0-angular';
import { Router, ActivatedRoute } from '@angular/router';

@Component({
  selector: 'app-error',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './error.html'
})
export class ErrorComponent implements OnInit {
  public auth = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  
  errorMessage = '';

  ngOnInit() {
    this.route.queryParams.subscribe(params => {
      if (params['message']) {
        this.errorMessage = params['message'];
      }
    });

    this.auth.error$.subscribe(error => {
      if (error) {
        this.errorMessage = error.message;
      }
    });
  }

  volverALogin() {
    const lastOrgId = localStorage.getItem('last_org_id');
    this.auth.loginWithRedirect({
      authorizationParams: {
        prompt: 'login', // Forzamos pedir credenciales para romper la sesión SSO errónea
        ...(lastOrgId && { organization: lastOrgId })
      }
    });
  }
}
