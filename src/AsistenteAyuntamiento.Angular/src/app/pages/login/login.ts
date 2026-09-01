import { Component, OnInit, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '@auth0/auth0-angular';

@Component({
  selector: 'app-login',
  standalone: true,
  template: `
    <div class="flex h-screen w-full items-center justify-center">
      <div class="text-center">
        <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-primary mx-auto mb-4"></div>
        <p class="text-lg font-medium text-gray-600">Procesando invitación...</p>
      </div>
    </div>
  `
})
export class LoginComponent implements OnInit {
  private route = inject(ActivatedRoute);
  public auth = inject(AuthService);

  ngOnInit() {
    const params = this.route.snapshot.queryParams;
    const invitation = params['invitation'];
    const organization = params['organization'];
    
    const authParams: any = {};
    
    if (invitation) {
      authParams.invitation = invitation;
    }
    
    if (organization) {
      authParams.organization = organization;
      // Guardamos la org para futuros accesos directos
      localStorage.setItem('last_org_id', organization);
    }
    
    this.auth.loginWithRedirect({
      authorizationParams: Object.keys(authParams).length > 0 ? authParams : undefined
    });
  }
}
