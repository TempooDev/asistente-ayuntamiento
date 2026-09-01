import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuthService } from '@auth0/auth0-angular';
import { Router } from '@angular/router';

@Component({
  selector: 'app-error',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="flex h-screen w-full flex-col items-center justify-center p-4 text-center">
      <div class="mb-6 rounded-full bg-red-100 p-4">
        <svg class="h-12 w-12 text-red-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
        </svg>
      </div>
      <h1 class="mb-2 text-3xl font-bold text-gray-900">Error de Autenticación</h1>
      <p class="mb-8 max-w-md text-lg text-gray-600">
        {{ errorMessage || 'Ha ocurrido un error durante el inicio de sesión. Por favor, verifica tu cuenta o contacta al administrador.' }}
      </p>
      
      <div class="flex gap-4">
        <button 
          (click)="volverALogin()" 
          class="rounded-md bg-blue-600 px-4 py-2 font-medium text-white hover:bg-blue-700">
          Volver a intentar
        </button>
      </div>
    </div>
  `
})
export class ErrorComponent implements OnInit {
  public auth = inject(AuthService);
  private router = inject(Router);
  
  errorMessage = '';

  ngOnInit() {
    this.auth.error$.subscribe(error => {
      if (error) {
        this.errorMessage = error.message;
      }
    });
  }

  volverALogin() {
    this.auth.loginWithRedirect({
      authorizationParams: {
        prompt: 'login' // Forzamos pedir credenciales para romper la sesión SSO errónea
      }
    });
  }
}
