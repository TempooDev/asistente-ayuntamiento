import { ChangeDetectionStrategy, Component, ElementRef, Injector, OnInit, afterNextRender, inject, signal, viewChild } from '@angular/core';
import { ConfirmService } from '../../../shared/ui/confirm.service';
import { ToastService } from '../../../shared/ui/toast.service';
import { ChatInputComponent } from '../chat-input/chat-input.component';
import { ChatStore } from '../data-access/chat.store';
import { MessageListComponent } from '../message-list/message-list.component';
import { ArenaVote } from '../models/chat.models';
import { SessionSidebarComponent } from '../session-sidebar/session-sidebar.component';
import { WelcomeGuideComponent } from '../welcome-guide/welcome-guide.component';

const WELCOME_SEEN_KEY = 'has_seen_welcome_guide';

/** Chat page: wires the store to the sidebar, the message list and the input. */
@Component({
  selector: 'app-chat-page',
  imports: [SessionSidebarComponent, MessageListComponent, ChatInputComponent, WelcomeGuideComponent],
  templateUrl: './chat-page.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChatPageComponent implements OnInit {
  protected readonly store = inject(ChatStore);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly injector = inject(Injector);
  private readonly input = viewChild(ChatInputComponent);
  private readonly sidebar = viewChild.required(SessionSidebarComponent);
  private readonly menuButton = viewChild<ElementRef<HTMLButtonElement>>('menuButton');

  protected readonly sidebarOpen = signal(false);
  protected readonly showWelcomeGuide = signal(!readFlag(WELCOME_SEEN_KEY));

  async ngOnInit(): Promise<void> {
    await this.run(() => this.store.init(), 'No se han podido cargar tus conversaciones.');
    this.input()?.focus();
  }

  /** Mobile only: opens the list and moves focus into it, since it sits before the menu button in tab order. */
  protected openSidebar(): void {
    this.sidebarOpen.set(true);
    afterNextRender(() => this.sidebar().focusFirst(), { injector: this.injector });
  }

  /** Focus inside the list would be lost when it turns invisible, so it goes back to the menu button first. */
  protected closeSidebar(): void {
    if (!this.sidebarOpen()) return;
    if (this.sidebar().hasFocus()) {
      this.menuButton()?.nativeElement.focus();
    }
    this.sidebarOpen.set(false);
  }

  protected async select(id: string): Promise<void> {
    this.closeSidebar();
    await this.run(() => this.store.selectSession(id), 'No se ha podido abrir la conversación.');
    this.input()?.focus();
  }

  protected async create(): Promise<void> {
    this.closeSidebar();
    await this.run(() => this.store.createSession(), 'No se ha podido crear la conversación.');
    this.input()?.focus();
  }

  protected async delete(id: string): Promise<void> {
    const confirmed = await this.confirm.ask({
      title: 'Eliminar chat',
      message: '¿Seguro que quieres eliminar esta conversación? Se borrará todo su historial y no se puede deshacer.',
      confirmLabel: 'Eliminar',
      danger: true,
    });
    if (confirmed) {
      await this.run(() => this.store.deleteSession(id), 'No se ha podido eliminar la conversación.');
    }
  }

  protected async send(text: string): Promise<void> {
    await this.store.send(text);
    this.input()?.focus();
  }

  protected async vote(event: { messageId: string; winner: ArenaVote }): Promise<void> {
    await this.run(() => this.store.vote(event.messageId, event.winner), 'No se ha podido registrar tu voto.');
  }

  protected closeWelcomeGuide(): void {
    this.showWelcomeGuide.set(false);
    writeFlag(WELCOME_SEEN_KEY);
  }

  private async run(action: () => Promise<unknown>, failure: string): Promise<void> {
    try {
      await action();
    } catch (err) {
      console.error(err);
      this.toast.error(failure);
    }
  }
}

function readFlag(key: string): boolean {
  try {
    return localStorage.getItem(key) === 'true';
  } catch {
    return false;
  }
}

function writeFlag(key: string): void {
  try {
    localStorage.setItem(key, 'true');
  } catch {
    // Not persisted: the guide shows again next time.
  }
}
