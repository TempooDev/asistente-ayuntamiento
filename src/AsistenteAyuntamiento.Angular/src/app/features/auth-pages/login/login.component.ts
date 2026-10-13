import { ChangeDetectionStrategy, Component, OnInit, inject, input } from '@angular/core';
import { AuthFacade } from '../../../core/auth/auth-facade.service';

/** Entry point of invitation and organization links: /login?invitation=…&organization=… */
@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginComponent implements OnInit {
  private readonly auth = inject(AuthFacade);

  readonly invitation = input<string>();
  readonly organization = input<string>();

  ngOnInit(): void {
    this.auth.loginWithOrg({ invitation: this.invitation(), organization: this.organization() });
  }
}
