import { Injectable, inject } from '@angular/core';
import { AuthService } from '@auth0/auth0-angular';
import * as signalR from '@microsoft/signalr';
import { Subject, Observable, firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

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

@Injectable({
  providedIn: 'root'
})
export class ChatService {
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
      .withUrl('/hubs/chat', {
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

  public async disconnect(): Promise<void> {
    if (this.hubConnection) {
      await this.hubConnection.stop();
      this.hubConnection = null;
    }
  }
}
