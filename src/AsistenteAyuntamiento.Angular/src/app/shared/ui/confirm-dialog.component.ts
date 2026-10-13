import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ConfirmService } from './confirm.service';
import { DialogFocusDirective } from './dialog-focus.directive';

@Component({
  selector: 'app-confirm-dialog',
  imports: [DialogFocusDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (confirm.pending(); as request) {
      <div class="fixed inset-0 bg-black/50 z-150 flex items-center justify-center p-4" (click)="confirm.answer(false)">
        <div
          class="bg-surface rounded-xl shadow-lg max-w-sm w-full p-6"
          role="alertdialog"
          aria-modal="true"
          aria-labelledby="confirm-title"
          aria-describedby="confirm-message"
          appDialogFocus
          (escape)="confirm.answer(false)"
          (click)="$event.stopPropagation()">
          <h2 id="confirm-title" class="font-headline-md text-headline-md font-bold mb-3">{{ request.title }}</h2>
          <p id="confirm-message" class="text-on-surface-variant font-body-md mb-6">{{ request.message }}</p>
          <div class="flex justify-end gap-3">
            <button type="button" class="px-4 py-2 border border-outline rounded-lg hover:bg-surface-variant font-label-md" (click)="confirm.answer(false)">Cancelar</button>
            <button
              type="button"
              class="px-4 py-2 rounded-lg font-label-md"
              [class]="request.danger ? 'bg-error text-on-error' : 'bg-primary text-on-primary'"
              (click)="confirm.answer(true)">
              {{ request.confirmLabel ?? 'Aceptar' }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
})
export class ConfirmDialogComponent {
  protected readonly confirm = inject(ConfirmService);
}
