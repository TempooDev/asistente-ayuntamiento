import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Auth0 redirect target; the SDK completes the login and navigates on. */
@Component({
  selector: 'app-callback',
  templateUrl: './callback.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CallbackComponent {}
