import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuthService } from '@auth0/auth0-angular';
import { Router, ActivatedRoute } from '@angular/router';

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
      
      <div class="flex flex-col items-center gap-4">
        <button 
          (click)="volverALogin()" 
          class="rounded-md bg-blue-600 px-4 py-2 font-medium text-white hover:bg-blue-700">
          Volver a intentar
        </button>

        <div class="mt-8 border-t pt-8">
          <p class="text-sm font-bold text-gray-500 mb-2">HERRAMIENTA PARA DESARROLLADORES</p>
          <p class="text-xs text-gray-400 mb-4 max-w-sm">Si no tienes invitación, pega aquí tu Organization ID (org_...) para entrar a la fuerza.</p>
          <div class="flex gap-2 justify-center">
            <input #orgInput type="text" placeholder="org_xxxxxxxxx" class="border rounded px-3 py-1 text-sm outline-none focus:border-blue-500">
            <button (click)="forzarLogin(orgInput.value)" class="bg-gray-800 text-white text-sm px-3 py-1 rounded hover:bg-gray-700">Entrar forzado</button>
          </div>
        </div>
      </div>
    </div>
  `
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

  forzarLogin(orgId: string) {
    if (!orgId || !orgId.startsWith('org_')) {
      alert('Debes introducir un ID válido que empiece por org_');
      return;
    }
    localStorage.setItem('last_org_id', orgId);
    this.auth.loginWithRedirect({
      authorizationParams: { organization: orgId }
    });
  }
}
