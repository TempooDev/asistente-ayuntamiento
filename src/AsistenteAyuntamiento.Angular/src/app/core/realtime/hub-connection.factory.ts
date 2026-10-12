import { Injectable, inject } from '@angular/core';
import { AuthService } from '@auth0/auth0-angular';
import * as signalR from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

/**
 * A single SignalR connection to one hub, safe to share between services and components.
 * `ensureConnected()` is idempotent: concurrent callers share the same in-flight start.
 */
export class ManagedHub {
  private starting: Promise<void> | null = null;

  constructor(readonly connection: signalR.HubConnection) {}

  get isConnected(): boolean {
    return this.connection.state === signalR.HubConnectionState.Connected;
  }

  ensureConnected(): Promise<void> {
    switch (this.connection.state) {
      case signalR.HubConnectionState.Connected:
        return Promise.resolve();
      case signalR.HubConnectionState.Disconnected:
        this.starting ??= this.connection.start().finally(() => (this.starting = null));
        return this.starting;
      default:
        // Connecting / Reconnecting / Disconnecting: wait for the transition instead of starting a second connection.
        return this.starting ?? this.waitUntilSettled();
    }
  }

  private async waitUntilSettled(timeoutMs = 30_000): Promise<void> {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      await new Promise(resolve => setTimeout(resolve, 250));
      if (this.connection.state === signalR.HubConnectionState.Connected) return;
      if (this.connection.state === signalR.HubConnectionState.Disconnected) return this.ensureConnected();
    }
    throw new Error('SignalR connection did not become available in time.');
  }
}

/**
 * Creates one connection per hub path. Each (re)connection asks Auth0 for a fresh access token,
 * so automatic reconnects keep working after the original token expires.
 */
@Injectable({ providedIn: 'root' })
export class HubConnectionFactory {
  private readonly auth = inject(AuthService);
  private readonly hubs = new Map<string, ManagedHub>();

  /** Returns the hub for `path`, creating it (and calling `configure` to register handlers) only once. */
  get(path: string, configure?: (connection: signalR.HubConnection) => void): ManagedHub {
    let hub = this.hubs.get(path);
    if (!hub) {
      const connection = new signalR.HubConnectionBuilder()
        .withUrl(`${environment.apiBaseUrl}${path}`, {
          accessTokenFactory: () => firstValueFrom(this.auth.getAccessTokenSilently()),
        })
        .withAutomaticReconnect()
        .build();
      configure?.(connection);
      hub = new ManagedHub(connection);
      this.hubs.set(path, hub);
    }
    return hub;
  }

  /** Stops every hub connection (call before logging out). */
  async stopAll(): Promise<void> {
    await Promise.allSettled([...this.hubs.values()].map(hub => hub.connection.stop()));
  }
}
