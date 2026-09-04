import { Component, OnInit, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '@auth0/auth0-angular';

@Component({
  selector: 'app-login',
  standalone: true,
  templateUrl: './login.html'
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
