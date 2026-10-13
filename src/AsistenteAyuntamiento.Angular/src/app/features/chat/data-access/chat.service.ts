import { Injectable, inject } from '@angular/core';
import { IStreamResult } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { HubConnectionFactory } from '../../../core/realtime/hub-connection.factory';
import { ArenaStreamChunk, ArenaVote, ChatMessageDto, ChatSessionSummaryDto, ConnectionState } from '../models/chat.models';

/** Stateless client of the chat hub (/hubs/chat). State lives in ChatStore. */
@Injectable({ providedIn: 'root' })
export class ChatService {
  private readonly receivedSource = new Subject<string>();
  /** Assistant messages pushed by the server outside a stream. */
  readonly messageReceived$ = this.receivedSource.asObservable();
  private readonly connectionSource = new Subject<ConnectionState>();
  /** Changes made by SignalR's automatic reconnection (the phone was locked, the network changed…). */
  readonly connectionChanges$ = this.connectionSource.asObservable();

  private readonly hub = inject(HubConnectionFactory).get('/hubs/chat', connection => {
    connection.on('ReceiveMessage', (message: string) => this.receivedSource.next(message));
    connection.onreconnecting(() => this.connectionSource.next('reconnecting'));
    connection.onreconnected(() => this.connectionSource.next('connected'));
    connection.onclose(() => this.connectionSource.next('disconnected'));
  });

  get isConnected(): boolean {
    return this.hub.isConnected;
  }

  /** Idempotent; the access token is refreshed on every (re)connection by the hub factory. */
  connect(): Promise<void> {
    return this.hub.ensureConnected();
  }

  getSessions(): Promise<ChatSessionSummaryDto[]> {
    return this.hub.connection.invoke('GetSessions');
  }

  loadSession(sessionId: string): Promise<ChatMessageDto[]> {
    return this.hub.connection.invoke('LoadSession', sessionId);
  }

  createSession(): Promise<string> {
    return this.hub.connection.invoke('CreateNewSession');
  }

  deleteSession(sessionId: string): Promise<void> {
    return this.hub.connection.invoke('DeleteSession', sessionId);
  }

  streamMessage(sessionId: string, message: string): IStreamResult<string> {
    return this.hub.connection.stream('StreamMessage', sessionId, message);
  }

  streamArenaMessage(sessionId: string, message: string): IStreamResult<ArenaStreamChunk> {
    return this.hub.connection.stream('StreamArenaMessage', sessionId, message);
  }

  voteArena(chatSessionId: string, battleId: string, winner: ArenaVote): Promise<void> {
    return this.hub.connection.invoke('VoteArenaMessage', { chatSessionId, battleId, winner });
  }
}
