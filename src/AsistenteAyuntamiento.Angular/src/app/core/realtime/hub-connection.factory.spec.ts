import type { HubConnection } from '@microsoft/signalr';
import { ManagedHub } from './hub-connection.factory';

/** Connection double with the states SignalR reports. */
function fakeConnection() {
  const connection = {
    state: 'Disconnected',
    start: vi.fn(async () => {
      connection.state = 'Connected';
    }),
    stop: vi.fn(async () => {
      connection.state = 'Disconnected';
    }),
  };
  return connection;
}

describe('ManagedHub', () => {
  it('creates nothing until the first connection is requested', async () => {
    const create = vi.fn(async () => fakeConnection() as unknown as HubConnection);
    const hub = new ManagedHub(create);

    expect(create).not.toHaveBeenCalled();
    expect(hub.isConnected).toBe(false);
    expect(() => hub.connection).toThrow(/ensureConnected/);
    await expect(hub.stop()).resolves.toBeUndefined();
  });

  it('creates and starts the connection once for concurrent callers', async () => {
    const connection = fakeConnection();
    const create = vi.fn(async () => connection as unknown as HubConnection);
    const hub = new ManagedHub(create);

    await Promise.all([hub.ensureConnected(), hub.ensureConnected(), hub.ensureConnected()]);

    expect(create).toHaveBeenCalledTimes(1);
    expect(connection.start).toHaveBeenCalledTimes(1);
    expect(hub.isConnected).toBe(true);
    expect(hub.connection).toBe(connection);
  });

  it('starts the same connection again after it was closed', async () => {
    const connection = fakeConnection();
    const create = vi.fn(async () => connection as unknown as HubConnection);
    const hub = new ManagedHub(create);
    await hub.ensureConnected();

    await hub.stop();
    await hub.ensureConnected();

    expect(create).toHaveBeenCalledTimes(1);
    expect(connection.start).toHaveBeenCalledTimes(2);
  });
});
