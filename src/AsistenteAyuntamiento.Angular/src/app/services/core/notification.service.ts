import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject, firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '@auth0/auth0-angular';

@Injectable({
  providedIn: 'root'
})
export class NotificationService {
  private hubConnection: signalR.HubConnection | null = null;
  private auth = inject(AuthService);
  
  private scraperStateChangedSource = new Subject<{ isScraping: boolean, message: string }>();
  public scraperStateChanged$ = this.scraperStateChangedSource.asObservable();

  public get isConnected(): boolean {
    return this.hubConnection?.state === signalR.HubConnectionState.Connected;
  }

  public async connect(): Promise<void> {
    if (this.isConnected) return;

    // Obtener token
    const token = await firstValueFrom(this.auth.getAccessTokenSilently());

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiBaseUrl}/hubs/notifications`, {
        accessTokenFactory: () => token
      })
      .withAutomaticReconnect()
      .build();

    this.hubConnection.on('ScraperStateChanged', (data: { isScraping: boolean, message: string }) => {
      this.scraperStateChangedSource.next(data);
    });

    try {
      await this.hubConnection.start();
      console.log('Notification SignalR connected');
    } catch (err) {
      console.error('Error al conectar con Notification SignalR:', err);
    }
  }
}
