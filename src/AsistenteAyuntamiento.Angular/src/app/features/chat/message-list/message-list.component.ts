import { ChangeDetectionStrategy, Component, ElementRef, effect, input, output, viewChild } from '@angular/core';
import { MarkdownPipe } from '../../../shared/markdown/markdown.pipe';
import { ArenaMessageComponent } from '../arena-message/arena-message.component';
import { ArenaVote, ChatMessage } from '../models/chat.models';

/** Messages of the current conversation; follows new content to the bottom. */
@Component({
  selector: 'app-message-list',
  imports: [MarkdownPipe, ArenaMessageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex-1 min-h-0 relative block' },
  template: `
    <div #scroller class="h-full overflow-y-auto p-4 md:p-margin-desktop" aria-live="polite" [attr.aria-busy]="generating()">
      @if (loading()) {
        <div class="h-full flex flex-col items-center justify-center text-gray-400 max-w-md mx-auto text-center">
          <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-primary mb-4" aria-hidden="true"></div>
          <p class="text-lg font-medium text-gray-600 mb-2">Cargando conversación...</p>
        </div>
      } @else if (messages().length === 0) {
        <div class="h-full flex flex-col items-center justify-center text-gray-400 max-w-md mx-auto text-center">
          <span class="material-symbols-outlined text-[48px] mb-4 text-gray-300" aria-hidden="true">chat_bubble_outline</span>
          <p class="text-lg font-medium text-gray-600 mb-2">¿En qué puedo ayudarte hoy?</p>
          <p class="text-sm">Selecciona una conversación anterior o escribe un mensaje para comenzar una nueva.</p>
        </div>
      } @else {
        <div class="chat-container w-full max-w-3xl mx-auto pb-4">
          @for (msg of messages(); track msg.id; let last = $last) {
            <div class="w-full flex flex-col" [class.items-end]="msg.kind === 'user'" [class.items-start]="msg.kind !== 'user'">
              @switch (msg.kind) {
                @case ('user') {
                  <div class="chat-bubble shadow-sm citizen">
                    <p class="whitespace-pre-wrap m-0">{{ msg.text }}</p>
                  </div>
                }
                @case ('arena') {
                  <app-arena-message [message]="msg" [canVote]="!(last && generating())" (vote)="vote.emit({ messageId: msg.id, winner: $event })" />
                }
                @default {
                  <div class="chat-bubble shadow-sm assistant">
                    @if (msg.arenaWinner) {
                      <div class="text-xs font-bold text-green-600 mb-1">Ganador: {{ msg.arenaWinner }}</div>
                    }
                    <div class="markdown-content" [innerHTML]="msg.text | markdown"></div>
                  </div>
                }
              }
            </div>
          }
          @if (waiting()) {
            <div class="chat-bubble assistant flex items-center gap-2 shadow-sm" role="status">
              <span class="w-2 h-2 rounded-full bg-gray-500 animate-bounce" style="animation-delay: 0s" aria-hidden="true"></span>
              <span class="w-2 h-2 rounded-full bg-gray-500 animate-bounce" style="animation-delay: 0.2s" aria-hidden="true"></span>
              <span class="w-2 h-2 rounded-full bg-gray-500 animate-bounce" style="animation-delay: 0.4s" aria-hidden="true"></span>
              <span class="text-sm text-gray-500 ml-1">Procesando...</span>
            </div>
          }
        </div>
      }
    </div>
  `,
})
export class MessageListComponent {
  readonly messages = input.required<readonly ChatMessage[]>();
  readonly loading = input(false);
  readonly waiting = input(false);
  readonly generating = input(false);
  readonly vote = output<{ messageId: string; winner: ArenaVote }>();

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  constructor() {
    // New messages or streamed text: keep the latest content in view.
    effect(() => {
      this.messages();
      this.waiting();
      const element = this.scroller()?.nativeElement;
      if (element) {
        queueMicrotask(() => (element.scrollTop = element.scrollHeight));
      }
    });
  }
}
