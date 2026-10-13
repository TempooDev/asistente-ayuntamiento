import { Injectable, inject, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { HubConnectionFactory } from '../../../core/realtime/hub-connection.factory';
import { ChatSessionSummaryDto, ChatMessageDto, ChatMessage, ArenaStreamChunk } from '../models/chat.models';

@Injectable({
  providedIn: 'root'
})
export class ChatService {
  public currentSessionId = signal<string>('');
  public messages = signal<ChatMessage[]>([]);
  public isGenerating = signal(false);
  public isWaitingForResponse = signal(false);
  public sessions = signal<ChatSessionSummaryDto[]>([]);
  
  // Cache to maintain state of background streams and loaded chats
  public sessionMessages = new Map<string, ChatMessage[]>();
  public activeStreams = new Map<string, any>();

  private messageReceivedSource = new Subject<string>();
  public messageReceived$ = this.messageReceivedSource.asObservable();

  private readonly hub = inject(HubConnectionFactory).get('/hubs/chat', connection => {
    connection.on('ReceiveMessage', (message: string) => this.messageReceivedSource.next(message));
  });

  private get hubConnection(): signalR.HubConnection {
    return this.hub.connection;
  }

  public get isConnected(): boolean {
    return this.hub.isConnected;
  }

  /** Idempotent; the access token is refreshed on every (re)connection by the hub factory. */
  public async connect(): Promise<void> {
    try {
      await this.hub.ensureConnected();
    } catch (err) {
      console.error('Error al conectar con SignalR:', err);
      throw err;
    }
  }

  public async getSessions(): Promise<ChatSessionSummaryDto[]> {
    if (this.isConnected) {
      return await this.hubConnection.invoke('GetSessions');
    }
    return [];
  }

  public async loadSession(sessionId: string): Promise<ChatMessageDto[]> {
    if (this.isConnected) {
      return await this.hubConnection.invoke('LoadSession', sessionId);
    }
    return [];
  }

  public async createNewSession(): Promise<string> {
    if (this.isConnected) {
      return await this.hubConnection.invoke('CreateNewSession');
    }
    return '';
  }

  // En SignalR de Angular, stream genera un objeto con método subscribe
  public streamMessage(sessionId: string, message: string): signalR.IStreamResult<string> {
    if (!this.isConnected) {
      throw new Error('Not connected');
    }
    return this.hubConnection.stream('StreamMessage', sessionId, message);
  }

  public streamArenaMessage(sessionId: string, message: string): signalR.IStreamResult<ArenaStreamChunk> {
    if (!this.isConnected) {
      throw new Error('Not connected');
    }
    return this.hubConnection.stream('StreamArenaMessage', sessionId, message);
  }

  public async voteArenaMessage(chatSessionId: string, battleId: string, winner: 'Alfa' | 'Beta' | 'Tie'): Promise<void> {
    if (this.isConnected) {
      await this.hubConnection.invoke('VoteArenaMessage', { chatSessionId, battleId, winner }).catch(err => console.warn('Vote mock/fallback', err));
    }
  }

  public async disconnect(): Promise<void> {
    await this.hubConnection.stop();
  }

  public async deleteSession(sessionId: string): Promise<void> {
    if (this.isConnected) {
      await this.hubConnection.invoke('DeleteSession', sessionId);
    }
  }
}
