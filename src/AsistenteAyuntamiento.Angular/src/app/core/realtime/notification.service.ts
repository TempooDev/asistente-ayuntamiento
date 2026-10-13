import { Injectable, inject } from '@angular/core';
import { Subject } from 'rxjs';
import { HubConnectionFactory } from './hub-connection.factory';

/** Payload of `ScraperStateChanged` (ScraperStateNotification in the API). */
export interface ScraperStateNotification {
  isScraping: boolean;
  message: string;
}

/** Payload of `DocumentStatusChanged` (DocumentStatusNotification in the API). */
export interface DocumentStatusNotification {
  documentId: string;
  status: string;
  errorMessage?: string;
}

@Injectable({
  providedIn: 'root'
})
export class NotificationService {
  private scraperStateChangedSource = new Subject<ScraperStateNotification>();
  public scraperStateChanged$ = this.scraperStateChangedSource.asObservable();

  private documentStatusChangedSource = new Subject<DocumentStatusNotification>();
  public documentStatusChanged$ = this.documentStatusChangedSource.asObservable();

  private readonly hub = inject(HubConnectionFactory).get('/hubs/notifications', connection => {
    connection.on('ScraperStateChanged', (data: ScraperStateNotification) => this.scraperStateChangedSource.next(data));
    connection.on('DocumentStatusChanged', (data: DocumentStatusNotification) => this.documentStatusChangedSource.next(data));
  });

  public get isConnected(): boolean {
    return this.hub.isConnected;
  }

  /** Idempotent: several pages can call it concurrently and only one connection is created. */
  public async connect(): Promise<void> {
    try {
      await this.hub.ensureConnected();
    } catch (err) {
      console.error('Error al conectar con Notification SignalR:', err);
    }
  }
}
