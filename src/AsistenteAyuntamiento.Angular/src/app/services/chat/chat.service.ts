import { Injectable, inject, signal } from '@angular/core';
import { AuthService } from '@auth0/auth0-angular';
import * as signalR from '@microsoft/signalr';
import { Subject, Observable, firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface ChatSessionSummaryDto {
  id: string;
  createdAt: string;
  preview: string;
  messageCount: number;
}

export interface ChatMessageDto {
  role: string;
  content: string;
  createdAt: string;
}

export interface ChatMessage {
  text: string;
  isUser: boolean;
  html?: string;
  isArena?: boolean;
  alfaText?: string;
  alfaHtml?: string;
  betaText?: string;
  betaHtml?: string;
  arenaResolved?: boolean;
  winner?: 'Alfa' | 'Beta' | 'Tie';
  battleId?: string;
}

export interface ArenaStreamChunk {
  option: 'Alfa' | 'Beta' | 'SessionId';
  content: string;
}

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

  private hubConnection: signalR.HubConnection | null = null;
  private auth = inject(AuthService);
  
  private messageReceivedSource = new Subject<string>();
  public messageReceived$ = this.messageReceivedSource.asObservable();

  public get isConnected(): boolean {
    return this.hubConnection?.state === signalR.HubConnectionState.Connected;
  }

  public async connect(): Promise<void> {
    if (this.isConnected) return;

    // Obtener token
    const token = await firstValueFrom(this.auth.getAccessTokenSilently());

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiBaseUrl}/hubs/chat`, {
        accessTokenFactory: () => token
      })
      .withAutomaticReconnect()
      .build();

    this.hubConnection.on('ReceiveMessage', (message: string) => {
      this.messageReceivedSource.next(message);
    });

    try {
      await this.hubConnection.start();
    } catch (err) {
      console.error('Error al conectar con SignalR:', err);
      throw err;
    }
  }

  public async getSessions(): Promise<ChatSessionSummaryDto[]> {
    if (this.isConnected) {
      return await this.hubConnection!.invoke('GetSessions');
    }
    return [];
  }

  public async loadSession(sessionId: string): Promise<ChatMessageDto[]> {
    if (this.isConnected) {
      return await this.hubConnection!.invoke('LoadSession', sessionId);
    }
    return [];
  }

  public async createNewSession(): Promise<string> {
    if (this.isConnected) {
      return await this.hubConnection!.invoke('CreateNewSession');
    }
    return '';
  }

  // En SignalR de Angular, stream genera un objeto con método subscribe
  public streamMessage(sessionId: string, message: string): signalR.IStreamResult<string> {
    if (!this.isConnected) {
      throw new Error('Not connected');
    }
    return this.hubConnection!.stream('StreamMessage', sessionId, message);
  }

  public streamArenaMessage(sessionId: string, message: string): signalR.IStreamResult<ArenaStreamChunk> {
    if (!this.isConnected) {
      throw new Error('Not connected');
    }
    return this.hubConnection!.stream('StreamArenaMessage', sessionId, message);
  }

  public async voteArenaMessage(chatSessionId: string, battleId: string, winner: 'Alfa' | 'Beta' | 'Tie'): Promise<void> {
    if (this.isConnected) {
      await this.hubConnection!.invoke('VoteArenaMessage', { chatSessionId, battleId, winner }).catch(err => console.warn('Vote mock/fallback', err));
    }
  }

  public async disconnect(): Promise<void> {
    if (this.hubConnection) {
      await this.hubConnection.stop();
      this.hubConnection = null;
    }
  }

  public async deleteSession(sessionId: string): Promise<void> {
    if (this.isConnected) {
      await this.hubConnection!.invoke('DeleteSession', sessionId);
    }
  }
}
