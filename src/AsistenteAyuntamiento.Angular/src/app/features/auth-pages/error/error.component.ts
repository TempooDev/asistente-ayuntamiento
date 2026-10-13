import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AuthService } from '@auth0/auth0-angular';
import { AuthFacade } from '../../../core/auth/auth-facade.service';
import { AUTH_ERROR_CODES, AUTH_ERROR_MESSAGES, AuthErrorCode } from '../../../core/auth/auth-error-codes';

const DEFAULT_MESSAGE = 'Ha ocurrido un error durante el inicio de sesión. Por favor, verifica tu cuenta o contacta al administrador.';

/** /error?code=… — the code selects a known message; free text from the URL is never shown. */
@Component({
  selector: 'app-error',
  templateUrl: './error.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ErrorComponent {
  private readonly facade = inject(AuthFacade);
  private readonly auth0Error = toSignal(inject(AuthService).error$, { initialValue: null });

  readonly code = input<string>();

  readonly errorMessage = computed(() => {
    const code = this.code() as AuthErrorCode | undefined;
    if (code && code in AUTH_ERROR_MESSAGES) return AUTH_ERROR_MESSAGES[code];
    return this.auth0Error()?.message ?? DEFAULT_MESSAGE;
  });

  retry(): void {
    if (this.code() === AUTH_ERROR_CODES.configUnavailable) {
      // The configuration is loaded at startup: reload to try again.
      window.location.assign('/');
      return;
    }
    // Force the credentials screen to break a wrong SSO session.
    this.facade.loginWithOrg({ prompt: 'login' });
  }
}
