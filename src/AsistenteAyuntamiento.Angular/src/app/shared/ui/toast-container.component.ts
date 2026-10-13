import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ToastService } from './toast.service';

@Component({
  selector: 'app-toast-container',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="fixed bottom-4 right-4 z-200 flex flex-col gap-2 max-w-sm" aria-live="polite" aria-atomic="false">
      @for (toast of toasts.toasts(); track toast.id) {
        <div
          class="flex items-start gap-3 rounded-lg px-4 py-3 shadow-lg text-sm"
          [class]="toast.kind === 'error' ? 'bg-error-container text-on-error-container' : toast.kind === 'success' ? 'bg-tertiary-fixed-dim text-on-tertiary-fixed-variant' : 'bg-surface-container-high text-on-surface'"
          [attr.role]="toast.kind === 'error' ? 'alert' : 'status'">
          <span class="flex-1">{{ toast.message }}</span>
          <button type="button" class="material-symbols-outlined text-[18px] opacity-70 hover:opacity-100" aria-label="Cerrar notificación" (click)="toasts.dismiss(toast.id)">close</button>
        </div>
      }
    </div>
  `,
})
export class ToastContainerComponent {
  protected readonly toasts = inject(ToastService);
}
