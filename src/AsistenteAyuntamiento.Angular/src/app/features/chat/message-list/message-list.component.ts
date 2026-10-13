import { ChangeDetectionStrategy, Component, ElementRef, effect, input, output, viewChild } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { interval, map, of, startWith, switchMap } from 'rxjs';
import { MarkdownPipe } from '../../../shared/markdown/markdown.pipe';
import { ArenaMessageComponent } from '../arena-message/arena-message.component';
import { ArenaVote, ChatMessage } from '../models/chat.models';

/** Shown in turn while waiting for the first chunk of the answer. */
export const WAITING_MESSAGES = ['Procesando mensaje...', 'Buscando referencias...', 'Redactando respuesta...'] as const;
export const WAITING_MESSAGE_INTERVAL_MS = 2500;
/** Distance from the bottom (px) under which the list keeps following new content. */
export const STICK_TO_BOTTOM_PX = 150;

/**
 * Messages of the current conversation. Follows new content to the bottom unless the user scrolled up to read;
 * sending a message or opening another conversation always jumps to the bottom.
 */
@Component({
  selector: 'app-message-list',
  imports: [MarkdownPipe, ArenaMessageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex-1 min-h-0 relative block' },
  template: `
    <div #scroller class="h-full overflow-y-auto p-4 md:p-margin-desktop" aria-live="polite" [attr.aria-busy]="generating()" (scroll)="onScroll()">
      @if (loading()) {
        <div class="h-full flex flex-col items-center justify-center text-gray-600 dark:text-gray-300 max-w-md mx-auto text-center">
          <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-primary mb-4" aria-hidden="true"></div>
          <p class="text-lg font-medium mb-2">Cargando conversación...</p>
        </div>
      } @else if (messages().length === 0) {
        <div class="h-full flex flex-col items-center justify-center text-gray-600 dark:text-gray-300 max-w-md mx-auto text-center">
          <span class="material-symbols-outlined text-[48px] mb-4 text-gray-300 dark:text-gray-500" aria-hidden="true">chat_bubble_outline</span>
          <p class="text-lg font-medium mb-2">¿En qué puedo ayudarte hoy?</p>
          <p class="text-sm">Selecciona una conversación anterior o escribe un mensaje para comenzar una nueva.</p>
        </div>
      } @else {
        <div class="chat-container w-full max-w-3xl mx-auto pb-4">
          @for (msg of messages(); track msg.id; let last = $last) {
            <div class="w-full flex flex-col" [class.items-end]="msg.kind === 'user'" [class.items-start]="msg.kind !== 'user'">
              @switch (msg.kind) {
                @case ('user') {
                  <div class="chat-bubble shadow-xs citizen">
                    <!-- Who says what is otherwise conveyed only by colour and position (WCAG 1.3.1). -->
                    <span class="sr-only">Tú: </span>
                    <p class="whitespace-pre-wrap m-0">{{ msg.text }}</p>
                  </div>
                }
                @case ('arena') {
                  <app-arena-message [message]="msg" [canVote]="!(last && generating())" (vote)="vote.emit({ messageId: msg.id, winner: $event })" />
                }
                @default {
                  <div class="chat-bubble shadow-xs assistant">
                    <span class="sr-only">Asistente: </span>
                    @if (msg.arenaWinner) {
                      <div class="text-xs font-bold text-green-800 dark:text-green-300 mb-1">Ganador: {{ msg.arenaWinner }}</div>
                    }
                    <div class="markdown-content" [innerHTML]="msg.text | markdown"></div>
                  </div>
                }
              }
            </div>
          }
          @if (waiting()) {
            <div class="chat-bubble assistant flex items-center gap-2 shadow-xs" role="status">
              <span class="w-2 h-2 rounded-full bg-gray-500 animate-bounce" style="animation-delay: 0s" aria-hidden="true"></span>
              <span class="w-2 h-2 rounded-full bg-gray-500 animate-bounce" style="animation-delay: 0.2s" aria-hidden="true"></span>
              <span class="w-2 h-2 rounded-full bg-gray-500 animate-bounce" style="animation-delay: 0.4s" aria-hidden="true"></span>
              <span class="text-sm text-gray-600 dark:text-gray-300 ml-1" data-testid="waiting-text">{{ waitingText() }}</span>
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

  /** Rotates while waiting; the interval stops with the first chunk (waiting turns false) or on destroy. */
  protected readonly waitingText = toSignal(
    toObservable(this.waiting).pipe(
      switchMap(waiting =>
        waiting
          ? interval(WAITING_MESSAGE_INTERVAL_MS).pipe(
              map(tick => WAITING_MESSAGES[(tick + 1) % WAITING_MESSAGES.length]),
              startWith(WAITING_MESSAGES[0]),
            )
          : of(WAITING_MESSAGES[0]),
      ),
    ),
    { initialValue: WAITING_MESSAGES[0] },
  );

  private followBottom = true;
  private previous: readonly ChatMessage[] = [];

  constructor() {
    effect(() => {
      const messages = this.messages();
      this.waiting();
      if (this.isNewConversation(messages) || this.userJustSent(messages)) {
        this.followBottom = true;
      }
      this.previous = messages;
      const element = this.scroller()?.nativeElement;
      if (element && this.followBottom) {
        queueMicrotask(() => (element.scrollTop = element.scrollHeight));
      }
    });
  }

  protected onScroll(): void {
    const element = this.scroller()?.nativeElement;
    if (!element) return;
    this.followBottom = element.scrollHeight - element.scrollTop - element.clientHeight < STICK_TO_BOTTOM_PX;
  }

  private isNewConversation(messages: readonly ChatMessage[]): boolean {
    return messages[0]?.id !== this.previous[0]?.id;
  }

  private userJustSent(messages: readonly ChatMessage[]): boolean {
    const known = new Set(this.previous.map(m => m.id));
    return messages.some(m => m.kind === 'user' && !known.has(m.id));
  }
}
