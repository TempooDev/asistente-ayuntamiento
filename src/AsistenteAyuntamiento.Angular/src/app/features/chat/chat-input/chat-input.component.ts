import { ChangeDetectionStrategy, Component, ElementRef, computed, input, output, signal, viewChild } from '@angular/core';

/** Message box: Enter sends, Shift+Enter adds a line. */
@Component({
  selector: 'app-chat-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="p-4 md:p-margin-desktop bg-white border-t border-gray-200 shrink-0">
      <form class="max-w-3xl mx-auto flex items-end gap-2" (submit)="$event.preventDefault(); submit()">
        <label for="chat-message" class="sr-only">Mensaje para el asistente</label>
        <textarea
          #box
          id="chat-message"
          class="gov-input flex-1 max-h-32 resize-none shadow-sm"
          rows="1"
          placeholder="Escribe tu consulta aquí..."
          [value]="text()"
          (input)="text.set(box.value)"
          (keydown.enter)="onEnter($event)"
          [disabled]="busy()"></textarea>
        <button
          type="submit"
          class="gov-btn gov-btn-primary shadow-sm"
          style="padding: 12px; min-height: unset; border-radius: 12px; margin-bottom: 2px"
          aria-label="Enviar mensaje"
          [disabled]="!canSend()">
          <span class="material-symbols-outlined text-[24px]" aria-hidden="true">send</span>
        </button>
      </form>
      @if (!connected()) {
        <div class="text-center mt-2 text-sm text-gray-500 flex items-center justify-center max-w-3xl mx-auto" role="status">
          <span class="w-2 h-2 rounded-full bg-red-500 mr-2 animate-pulse" aria-hidden="true"></span>
          Conectando al servidor...
        </div>
      }
    </div>
  `,
})
export class ChatInputComponent {
  readonly connected = input(true);
  readonly busy = input(false);
  readonly send = output<string>();

  protected readonly text = signal('');
  protected readonly canSend = computed(() => this.text().trim().length > 0 && this.connected() && !this.busy());
  private readonly box = viewChild.required<ElementRef<HTMLTextAreaElement>>('box');

  focus(): void {
    setTimeout(() => this.box().nativeElement.focus());
  }

  protected onEnter(event: Event): void {
    if ((event as KeyboardEvent).shiftKey) return;
    event.preventDefault();
    this.submit();
  }

  protected submit(): void {
    if (!this.canSend()) return;
    this.send.emit(this.text());
    this.text.set('');
  }
}
