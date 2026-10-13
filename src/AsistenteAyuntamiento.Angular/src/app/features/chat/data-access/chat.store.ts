import { Injectable, WritableSignal, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { ISubscription } from '@microsoft/signalr';
import { environment } from '../../../../environments/environment';
import { InactivityService } from '../../../core/auth/inactivity.service';
import {
  ArenaMessage,
  ArenaSide,
  ArenaStreamChunk,
  ArenaVote,
  AssistantMessage,
  ChatMessage,
  ChatSessionSummaryDto,
  ConnectionState,
} from '../models/chat.models';
import { ChatService } from './chat.service';
import { RECONNECT_FAILED_MESSAGE, mapStreamError } from './stream-errors';

/** Pending text per message and field, flushed into the state once per animation frame. */
type PendingDelta = { text?: string; alfa?: string; beta?: string; battleId?: string; failed?: readonly ArenaSide[] };
type PendingChunks = Map<string, PendingDelta>;

const nextFrame: (callback: () => void) => void =
  typeof requestAnimationFrame === 'function' ? cb => requestAnimationFrame(() => cb()) : cb => setTimeout(cb, 16);

let messageCounter = 0;
const newId = () => `m${++messageCounter}`;

/**
 * Chat state. All state is immutable signals; components only read it and call actions. Conversations are cached
 * per session and streams keep running when the user switches session or page (the store is a singleton).
 */
@Injectable({ providedIn: 'root' })
export class ChatStore {
  private readonly hub = inject(ChatService);
  private readonly inactivity = inject(InactivityService);

  private readonly messagesBySession = signal<ReadonlyMap<string, readonly ChatMessage[]>>(new Map());
  private readonly streaming = signal<ReadonlySet<string>>(new Set());
  private readonly waiting = signal<ReadonlySet<string>>(new Set());
  private readonly subscriptions = new Map<string, ISubscription<unknown>>();
  /** Arena messages whose vote is in flight (a second click must not send a second vote). */
  private readonly voting = new Set<string>();
  private pending: PendingChunks = new Map();
  private flushScheduled = false;

  readonly sessions = signal<readonly ChatSessionSummaryDto[]>([]);
  readonly currentSessionId = signal('');
  readonly connection = signal<ConnectionState>('connecting');
  readonly loadingHistory = signal(false);
  readonly arenaMode = signal(environment.enableArenaModeByDefault ?? false);

  readonly messages = computed(() => this.messagesBySession().get(this.currentSessionId()) ?? []);
  /** The current session is streaming an answer. */
  readonly isGenerating = computed(() => this.streaming().has(this.currentSessionId()));
  /** The current session is waiting for the first chunk. */
  readonly isWaiting = computed(() => this.waiting().has(this.currentSessionId()));
  readonly isConnected = computed(() => this.connection() === 'connected');

  constructor() {
    this.hub.messageReceived$.pipe(takeUntilDestroyed()).subscribe(text => this.receive(text));
    this.hub.connectionChanges$.pipe(takeUntilDestroyed()).subscribe(state => this.connection.set(state));
  }

  /** Connects and opens the latest conversation, unless one is already loaded (returning to the page). */
  async init(): Promise<void> {
    try {
      await this.hub.connect();
      this.connection.set('connected');
    } catch (err) {
      console.error('Chat hub connection failed', err);
      this.connection.set('disconnected');
      return;
    }
    if (this.currentSessionId() && this.messagesBySession().has(this.currentSessionId())) {
      return;
    }
    await this.loadSessions();
    const latest = this.sessions()[0];
    if (latest) {
      await this.selectSession(latest.id);
    }
  }

  async loadSessions(): Promise<void> {
    this.sessions.set(await this.hub.getSessions());
  }

  async selectSession(id: string): Promise<void> {
    this.currentSessionId.set(id);
    if (this.messagesBySession().has(id)) {
      return; // Cached: keeps streams that are still writing into it.
    }
    this.loadingHistory.set(true);
    try {
      const history = await this.hub.loadSession(id);
      this.setMessages(
        id,
        history.map(m =>
          m.role.toLowerCase() === 'user'
            ? { kind: 'user', id: newId(), text: m.content }
            : { kind: 'assistant', id: newId(), text: m.content },
        ),
      );
    } finally {
      this.loadingHistory.set(false);
    }
  }

  async createSession(): Promise<string> {
    const id = await this.hub.createSession();
    this.setMessages(id, []);
    this.currentSessionId.set(id);
    await this.loadSessions();
    return id;
  }

  async deleteSession(id: string): Promise<void> {
    await this.hub.deleteSession(id);
    this.subscriptions.get(id)?.dispose();
    this.subscriptions.delete(id);
    this.messagesBySession.update(map => {
      const next = new Map(map);
      next.delete(id);
      return next;
    });
    if (this.currentSessionId() === id) {
      await this.createSession();
    } else {
      await this.loadSessions();
    }
  }

  toggleArenaMode(): void {
    this.arenaMode.update(on => !on);
  }

  /**
   * Sends text in the current session (creating one if needed) and streams the answer. A dropped connection (for
   * example after the phone was locked) is re-established first instead of blocking the message.
   */
  async send(text: string): Promise<void> {
    const message = text.trim();
    if (!message || this.isGenerating()) return;

    if (!(await this.reconnect())) {
      const sessionId = this.currentSessionId();
      this.updateMessages(sessionId, list => [
        ...list,
        { kind: 'user', id: newId(), text: message },
        { kind: 'assistant', id: newId(), text: `**⚠️ ${RECONNECT_FAILED_MESSAGE}**` },
      ]);
      return;
    }

    const sessionId = this.currentSessionId() || (await this.createSession());
    const answer: AssistantMessage | ArenaMessage = this.arenaMode()
      ? { kind: 'arena', id: newId(), alfa: '', beta: '' }
      : { kind: 'assistant', id: newId(), text: '' };
    this.updateMessages(sessionId, list => [...list, { kind: 'user', id: newId(), text: message }, answer]);
    this.mark(this.streaming, sessionId, true);
    this.mark(this.waiting, sessionId, true);
    // The session must not expire while the answer is still arriving.
    const releaseHold = this.inactivity.hold();

    const finish = (error?: unknown) => {
      releaseHold();
      if (error !== undefined) {
        console.error('Chat stream failed', error);
        const notice = mapStreamError(error);
        // A failed Arena stream is not stored as a battle, so neither side can be voted on.
        this.queue(answer.id, answer.kind === 'arena' ? { alfa: notice, beta: notice, failed: ['Alfa', 'Beta'] } : { text: notice });
      }
      this.flush();
      this.subscriptions.delete(sessionId);
      this.mark(this.streaming, sessionId, false);
      this.mark(this.waiting, sessionId, false);
      void this.loadSessions().catch(() => undefined);
    };

    try {
      const subscription =
        answer.kind === 'arena'
          ? this.hub.streamArenaMessage(sessionId, message).subscribe({
              next: chunk => this.onArenaChunk(sessionId, answer.id, chunk),
              complete: () => finish(),
              error: err => finish(err),
            })
          : this.hub.streamMessage(sessionId, message).subscribe({
              next: chunk => {
                this.mark(this.waiting, sessionId, false);
                this.queue(answer.id, { text: chunk });
              },
              complete: () => finish(),
              error: err => finish(err),
            });
      this.subscriptions.set(sessionId, subscription);
    } catch (err) {
      finish(err);
    }
  }

  /**
   * Records the vote (once: votes are final) and replaces the two answers with the one the server stored in the
   * conversation (the chosen side, or both for a tie), so a reload shows the same. Battles with a failed side
   * cannot be voted on.
   */
  async vote(messageId: string, winner: ArenaVote): Promise<void> {
    const sessionId = this.currentSessionId();
    const battle = this.messages().find((m): m is ArenaMessage => m.id === messageId && m.kind === 'arena');
    if (!battle?.battleId || battle.failed?.length || this.voting.has(messageId)) return;

    this.voting.add(messageId);
    try {
      const text = await this.hub.voteArena(sessionId, battle.battleId, winner);
      if (text == null) return;
      this.updateMessages(sessionId, list =>
        list.map(m => (m.id === messageId ? { kind: 'assistant', id: m.id, text, arenaWinner: winner } : m)),
      );
    } finally {
      this.voting.delete(messageId);
    }
  }

  /** True when the hub is (or could be made) connected; shows "reconnecting" meanwhile. */
  private async reconnect(): Promise<boolean> {
    if (this.hub.isConnected) {
      this.connection.set('connected');
      return true;
    }
    this.connection.set('reconnecting');
    try {
      await this.hub.connect();
      this.connection.set('connected');
      return true;
    } catch (err) {
      console.error('Chat hub reconnection failed', err);
      this.connection.set('disconnected');
      return false;
    }
  }

  private receive(text: string): void {
    const sessionId = this.currentSessionId();
    this.mark(this.waiting, sessionId, false);
    this.updateMessages(sessionId, list => [...list, { kind: 'assistant', id: newId(), text }]);
    void this.loadSessions().catch(() => undefined);
  }

  private onArenaChunk(sessionId: string, messageId: string, chunk: ArenaStreamChunk): void {
    if (chunk.option === 'SessionId') {
      this.queue(messageId, { battleId: chunk.content });
      return;
    }
    this.mark(this.waiting, sessionId, false);
    if (chunk.errorCode) {
      // That side failed: keep its partial text, show the typed error and block the vote.
      const notice = mapStreamError(`[${chunk.errorCode}]`);
      this.queue(messageId, chunk.option === 'Alfa' ? { alfa: notice, failed: ['Alfa'] } : { beta: notice, failed: ['Beta'] });
      return;
    }
    this.queue(messageId, chunk.option === 'Alfa' ? { alfa: chunk.content } : { beta: chunk.content });
  }

  /** Buffers streamed text; the state (and the Markdown render) changes at most once per frame. */
  private queue(messageId: string, delta: PendingDelta): void {
    const current = this.pending.get(messageId) ?? {};
    this.pending.set(messageId, {
      text: (current.text ?? '') + (delta.text ?? ''),
      alfa: (current.alfa ?? '') + (delta.alfa ?? ''),
      beta: (current.beta ?? '') + (delta.beta ?? ''),
      battleId: delta.battleId ?? current.battleId,
      failed: [...(current.failed ?? []), ...(delta.failed ?? [])],
    });
    if (!this.flushScheduled) {
      this.flushScheduled = true;
      nextFrame(() => this.flush());
    }
  }

  private flush(): void {
    this.flushScheduled = false;
    if (this.pending.size === 0) return;
    const pending = this.pending;
    this.pending = new Map();

    this.messagesBySession.update(map => {
      const next = new Map(map);
      for (const [sessionId, list] of map) {
        if (!list.some(m => pending.has(m.id))) continue;
        next.set(
          sessionId,
          list.map(m => {
            const delta = pending.get(m.id);
            if (!delta) return m;
            if (m.kind === 'arena') {
              const failed = delta.failed?.length ? [...new Set([...(m.failed ?? []), ...delta.failed])] : m.failed;
              return { ...m, alfa: m.alfa + (delta.alfa ?? ''), beta: m.beta + (delta.beta ?? ''), battleId: delta.battleId ?? m.battleId, failed };
            }
            return m.kind === 'assistant' ? { ...m, text: m.text + (delta.text ?? '') } : m;
          }),
        );
      }
      return next;
    });
  }

  private setMessages(sessionId: string, messages: readonly ChatMessage[]): void {
    this.messagesBySession.update(map => new Map(map).set(sessionId, messages));
  }

  private updateMessages(sessionId: string, change: (list: readonly ChatMessage[]) => readonly ChatMessage[]): void {
    this.messagesBySession.update(map => new Map(map).set(sessionId, change(map.get(sessionId) ?? [])));
  }

  private mark(set: WritableSignal<ReadonlySet<string>>, sessionId: string, on: boolean): void {
    set.update(current => {
      if (current.has(sessionId) === on) return current;
      const next = new Set(current);
      if (on) next.add(sessionId);
      else next.delete(sessionId);
      return next;
    });
  }
}
