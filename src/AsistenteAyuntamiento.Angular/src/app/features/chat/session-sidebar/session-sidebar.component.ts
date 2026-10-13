import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { ChatSessionSummaryDto } from '../models/chat.models';

/**
 * List of the user's conversations; on mobile it slides in over the chat. Closed on mobile it is also invisible, so
 * keyboard and screen reader users do not land on controls they cannot see.
 */
@Component({
  selector: 'app-session-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'contents' },
  template: `
    <nav
      aria-label="Conversaciones"
      class="chat-sidebar bg-surface-container-lowest border-r border-gray-200 flex flex-col w-[280px] shrink-0 transition-[transform,visibility] duration-300 md:translate-x-0 absolute md:relative z-10 h-full shadow-lg md:shadow-none"
      [class.translate-x-0]="open()"
      [class.-translate-x-full]="!open()"
      [class.max-md:invisible]="!open()">
      <div class="chat-sidebar-header p-4 border-b border-gray-100 sticky top-0 bg-surface-container-lowest z-20 flex justify-between items-center">
        <button type="button" class="gov-btn gov-btn-primary w-full flex items-center justify-center gap-2" (click)="create.emit()">
          <span class="material-symbols-outlined text-[20px]" aria-hidden="true">add</span>
          Nuevo Chat
        </button>
        <button type="button" class="md:hidden ml-2 text-gray-500" aria-label="Cerrar lista de conversaciones" (click)="closed.emit()">
          <span class="material-symbols-outlined" aria-hidden="true">close</span>
        </button>
      </div>
      <div class="flex-1 overflow-y-auto p-2">
        @if (sessions().length === 0) {
          <div class="text-center text-sm text-gray-500 py-4">No hay chats recientes</div>
        } @else {
          <ul class="space-y-1">
            @for (session of sessions(); track session.id) {
              <li
                class="chat-session-item rounded-lg hover:bg-gray-50 transition-colors flex items-start"
                [class.bg-blue-50]="currentSessionId() === session.id"
                [class.border-l-4]="currentSessionId() === session.id"
                [class.border-blue-600]="currentSessionId() === session.id">
                <button
                  type="button"
                  class="flex-1 text-left p-3 min-w-0"
                  [attr.aria-current]="currentSessionId() === session.id ? 'true' : null"
                  (click)="selected.emit(session.id)">
                  <span class="flex justify-between items-center mb-1 gap-2">
                    <span class="text-xs font-medium text-gray-600">{{ formatDate(session.createdAt) }}</span>
                    @if (session.messageCount > 0) {
                      <span class="text-[10px] bg-gray-100 text-gray-600 px-2 py-0.5 rounded-full">{{ session.messageCount }} msgs</span>
                    }
                  </span>
                  <span class="block text-sm text-gray-800 line-clamp-2 leading-tight">{{ session.preview || 'Nueva conversación' }}</span>
                </button>
                <button
                  type="button"
                  class="text-gray-500 hover:text-red-600 dark:hover:text-red-300 transition-colors p-3"
                  [attr.aria-label]="'Eliminar conversación: ' + (session.preview || 'Nueva conversación')"
                  title="Eliminar chat"
                  (click)="deleted.emit(session.id)">
                  <span class="material-symbols-outlined text-[16px]" aria-hidden="true">delete</span>
                </button>
              </li>
            }
          </ul>
        }
      </div>
    </nav>
  `,
})
export class SessionSidebarComponent {
  readonly sessions = input.required<readonly ChatSessionSummaryDto[]>();
  readonly currentSessionId = input('');
  readonly open = input(false);

  readonly selected = output<string>();
  readonly create = output<void>();
  readonly deleted = output<string>();
  readonly closed = output<void>();

  formatDate(value: string): string {
    const date = new Date(value);
    const today = new Date();
    if (date.toDateString() === today.toDateString()) {
      return 'Hoy, ' + date.toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' });
    }
    const yesterday = new Date(today);
    yesterday.setDate(today.getDate() - 1);
    if (date.toDateString() === yesterday.toDateString()) {
      return 'Ayer';
    }
    return date.toLocaleDateString('es-ES', { day: '2-digit', month: 'short' });
  }
}
