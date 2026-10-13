import { Injectable, inject } from '@angular/core';
import { AuthService } from '@auth0/auth0-angular';
import type { HubConnection } from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

/** `HubConnectionState` values; compared as strings so the SignalR module is not needed at startup. */
type HubState = 'Disconnected' | 'Connecting' | 'Connected' | 'Disconnecting' | 'Reconnecting';

/**
 * A single SignalR connection to one hub, safe to share between services and components. The connection (and the
 * SignalR library, ~55 kB) is created on the first `ensureConnected()`, so pages without real-time features never
 * load it. `ensureConnected()` is idempotent: concurrent callers share the same in-flight start.
 */
export class ManagedHub {
  private hub: HubConnection | null = null;
  private creating: Promise<HubConnection> | null = null;
  private starting: Promise<void> | null = null;

  constructor(private readonly create: () => Promise<HubConnection>) {}

  /** The connection; only available once `ensureConnected()` has been called. */
  get connection(): HubConnection {
    if (!this.hub) throw new Error('The hub is not connected yet: call ensureConnected() first.');
    return this.hub;
  }

  get isConnected(): boolean {
    return this.state === 'Connected';
  }

  private get state(): HubState {
    return (this.hub?.state as HubState | undefined) ?? 'Disconnected';
  }

  async ensureConnected(): Promise<void> {
    const connection = this.hub ?? (await (this.creating ??= this.create().then(created => (this.hub = created))));
    switch (this.state) {
      case 'Connected':
        return;
      case 'Disconnected':
        this.starting ??= connection.start().finally(() => (this.starting = null));
        return this.starting;
      default:
        // Connecting / Reconnecting / Disconnecting: wait for the transition instead of starting a second connection.
        return this.starting ?? this.waitUntilSettled();
    }
  }

  /** Stops the connection if it was ever created. */
  async stop(): Promise<void> {
    await this.hub?.stop();
  }

  private async waitUntilSettled(timeoutMs = 30_000): Promise<void> {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      await new Promise(resolve => setTimeout(resolve, 250));
      if (this.state === 'Connected') return;
      if (this.state === 'Disconnected') return this.ensureConnected();
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

  /**
   * Returns the hub for `path`. `configure` registers the handlers; it runs once, when the connection is created
   * on the first `ensureConnected()`.
   */
  get(path: string, configure?: (connection: HubConnection) => void): ManagedHub {
    let hub = this.hubs.get(path);
    if (!hub) {
      hub = new ManagedHub(async () => {
        const signalR = await import('@microsoft/signalr');
        const connection = new signalR.HubConnectionBuilder()
          .withUrl(`${environment.apiBaseUrl}${path}`, {
            accessTokenFactory: () => firstValueFrom(this.auth.getAccessTokenSilently()),
          })
          .withAutomaticReconnect()
          .build();
        configure?.(connection);
        return connection;
      });
      this.hubs.set(path, hub);
    }
    return hub;
  }

  /** Stops every hub connection that was created (call before logging out). */
  async stopAll(): Promise<void> {
    await Promise.allSettled([...this.hubs.values()].map(hub => hub.stop()));
  }
}
