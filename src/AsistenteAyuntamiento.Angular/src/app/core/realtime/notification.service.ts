import { Injectable, inject } from '@angular/core';
import { Observable, Subject, map, merge } from 'rxjs';
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

  private documentStatusesChangedSource = new Subject<DocumentStatusNotification[]>();

  /**
   * Every document status change as batches: `DocumentStatusesChanged` (enqueuing many documents at once) arrives
   * as one array, a single `DocumentStatusChanged` (the worker, one document at a time) as an array of one.
   */
  public readonly documentStatusUpdates$: Observable<DocumentStatusNotification[]> = merge(
    this.documentStatusChangedSource.pipe(map(change => [change])),
    this.documentStatusesChangedSource,
  );

  private readonly hub = inject(HubConnectionFactory).get('/hubs/notifications', connection => {
    connection.on('ScraperStateChanged', (data: ScraperStateNotification) => this.scraperStateChangedSource.next(data));
    connection.on('DocumentStatusChanged', (data: DocumentStatusNotification) => this.documentStatusChangedSource.next(data));
    connection.on('DocumentStatusesChanged', (data: DocumentStatusNotification[]) => this.documentStatusesChangedSource.next(data));
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
