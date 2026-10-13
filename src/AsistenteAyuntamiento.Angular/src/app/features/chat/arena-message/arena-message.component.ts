import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MarkdownPipe } from '../../../shared/markdown/markdown.pipe';
import { ArenaMessage, ArenaVote } from '../models/chat.models';

/** Two anonymous answers side by side; the user votes for one (or a tie). Votes are buttons, so keyboard works. */
@Component({
  selector: 'app-arena-message',
  imports: [MarkdownPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="w-full flex flex-col md:flex-row gap-4 mb-2" role="group" aria-label="Comparación de respuestas: elige la mejor">
      @for (side of sides; track side.vote) {
        @if (!$first) {
          <div class="flex items-center justify-center shrink-0">
            <button
              type="button"
              class="px-3 py-1 bg-white border border-gray-300 text-gray-600 rounded-full hover:bg-gray-100 text-xs shadow-xs transition-colors disabled:opacity-50"
              [disabled]="!canVote()"
              (click)="vote.emit('Tie')">
              Empate
            </button>
          </div>
        }
        <div class="chat-bubble shadow-xs assistant flex-1 relative group">
          <div class="text-xs font-bold text-gray-400 mb-1">Modelo {{ side.vote }}</div>
          <div class="markdown-content" [innerHTML]="(side.vote === 'Alfa' ? message().alfa : message().beta) | markdown"></div>
          <button
            type="button"
            class="mt-3 inline-flex items-center gap-2 px-3 py-1.5 rounded-full border border-primary text-primary text-sm font-medium hover:bg-primary/10 focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-50"
            [disabled]="!canVote()"
            [attr.aria-label]="'Elegir la respuesta del modelo ' + side.vote"
            (click)="vote.emit(side.vote)">
            <span class="material-symbols-outlined text-[18px]" aria-hidden="true">thumb_up</span>
            Elegir esta respuesta
          </button>
        </div>
      }
    </div>
  `,
})
export class ArenaMessageComponent {
  readonly message = input.required<ArenaMessage>();
  /** False while the answers are still streaming. */
  readonly canVote = input(true);
  readonly vote = output<ArenaVote>();

  protected readonly sides: readonly { vote: 'Alfa' | 'Beta' }[] = [{ vote: 'Alfa' }, { vote: 'Beta' }];
}
