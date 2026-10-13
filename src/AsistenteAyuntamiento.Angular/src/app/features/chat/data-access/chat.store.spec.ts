import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { InactivityService } from '../../../core/auth/inactivity.service';
import { ArenaStreamChunk, ArenaMessage, AssistantMessage, ConnectionState } from '../models/chat.models';
import { ChatService } from './chat.service';
import { ChatStore } from './chat.store';
import { RECONNECT_FAILED_MESSAGE, STREAM_ERROR_MESSAGES } from './stream-errors';

/** Stream double: the test pushes chunks, completes or fails it. */
class FakeStream<T> {
  observer?: { next: (v: T) => void; complete: () => void; error: (e: unknown) => void };
  disposed = false;
  subscribe(observer: { next: (v: T) => void; complete: () => void; error: (e: unknown) => void }) {
    this.observer = observer;
    return { dispose: () => (this.disposed = true) };
  }
}

class FakeChatService {
  readonly received = new Subject<string>();
  readonly messageReceived$ = this.received.asObservable();
  readonly connectionChanges = new Subject<ConnectionState>();
  readonly connectionChanges$ = this.connectionChanges.asObservable();
  isConnected = true;
  sessions = [{ id: 's1', createdAt: '2026-10-13T10:00:00Z', preview: 'Hola', messageCount: 2 }];
  textStream = new FakeStream<string>();
  arenaStream = new FakeStream<ArenaStreamChunk>();
  votes: unknown[] = [];
  connect = vi.fn(() => Promise.resolve());
  getSessions = vi.fn(() => Promise.resolve(this.sessions));
  loadSession = vi.fn(() => Promise.resolve([
    { role: 'user', content: '¿Plazos?', createdAt: '' },
    { role: 'assistant', content: 'Diez días', createdAt: '' },
  ]));
  createSession = vi.fn(() => Promise.resolve('new-session'));
  deleteSession = vi.fn(() => Promise.resolve());
  streamMessage = vi.fn(() => this.textStream);
  streamArenaMessage = vi.fn(() => this.arenaStream);
  voteArena = vi.fn((...args: unknown[]) => {
    this.votes.push(args);
    return Promise.resolve();
  });
}

class FakeInactivityService {
  activeHolds = 0;
  hold() {
    this.activeHolds++;
    let released = false;
    return () => {
      if (!released) this.activeHolds--;
      released = true;
    };
  }
}

const frame = () => new Promise(resolve => setTimeout(resolve, 20));

describe('ChatStore', () => {
  let hub: FakeChatService;
  let store: ChatStore;
  let inactivity: FakeInactivityService;

  beforeEach(() => {
    hub = new FakeChatService();
    inactivity = new FakeInactivityService();
    TestBed.configureTestingModule({
      providers: [
        { provide: ChatService, useValue: hub },
        { provide: InactivityService, useValue: inactivity },
      ],
    });
    store = TestBed.inject(ChatStore);
    store.arenaMode.set(false);
  });

  it('connects and opens the latest conversation', async () => {
    await store.init();

    expect(store.isConnected()).toBe(true);
    expect(store.currentSessionId()).toBe('s1');
    expect(store.messages().map(m => m.kind)).toEqual(['user', 'assistant']);
  });

  it('marks the connection as lost when the hub cannot connect', async () => {
    hub.connect.mockRejectedValueOnce(new Error('offline'));

    await store.init();

    expect(store.connection()).toBe('disconnected');
  });

  it('streams an answer, batching chunks per frame, and ends the stream state', async () => {
    await store.init();
    await store.send('¿Requisitos?');
    expect(store.isWaiting()).toBe(true);
    expect(store.isGenerating()).toBe(true);

    hub.textStream.observer!.next('Hola');
    hub.textStream.observer!.next(', mundo');
    const last = () => store.messages().at(-1) as AssistantMessage;
    expect(last().text).toBe(''); // Not rendered until the next frame.
    expect(store.isWaiting()).toBe(false);

    await frame();
    expect(last().text).toBe('Hola, mundo');

    hub.textStream.observer!.complete();
    expect(store.isGenerating()).toBe(false);
    expect(hub.getSessions).toHaveBeenCalledTimes(2);
  });

  it('keeps state immutable: a new chunk produces a new messages array', async () => {
    await store.init();
    await store.send('x');
    const before = store.messages();

    hub.textStream.observer!.next('y');
    await frame();

    expect(store.messages()).not.toBe(before);
    expect(before.at(-1)).toEqual(expect.objectContaining({ text: '' }));
  });

  it('appends a typed, user-facing error when the stream fails', async () => {
    await store.init();
    await store.send('x');

    hub.textStream.observer!.error(new Error('HubException: Error [INVALID_REQUEST]: la solicitud no es válida.'));

    const last = store.messages().at(-1) as AssistantMessage;
    expect(last.text).toContain(STREAM_ERROR_MESSAGES['INVALID_REQUEST']);
    expect(store.isGenerating()).toBe(false);
  });

  it('ignores empty messages and does not send while generating', async () => {
    await store.init();
    await store.send('   ');
    expect(hub.streamMessage).not.toHaveBeenCalled();

    await store.send('uno');
    await store.send('dos');
    expect(hub.streamMessage).toHaveBeenCalledTimes(1);
  });

  it('creates a session when there is none', async () => {
    hub.sessions = [];
    await store.init();

    await store.send('Hola');

    expect(hub.createSession).toHaveBeenCalled();
    expect(hub.streamMessage).toHaveBeenCalledWith('new-session', 'Hola');
  });

  it('fills both arena sides and resolves the vote to the chosen answer', async () => {
    store.arenaMode.set(true);
    await store.init();
    await store.send('Compara');

    hub.arenaStream.observer!.next({ option: 'SessionId', content: 'battle-1' });
    hub.arenaStream.observer!.next({ option: 'Alfa', content: 'Respuesta A' });
    hub.arenaStream.observer!.next({ option: 'Beta', content: 'Respuesta B' });
    hub.arenaStream.observer!.complete();

    const battle = store.messages().at(-1) as ArenaMessage;
    expect(battle).toEqual(expect.objectContaining({ kind: 'arena', battleId: 'battle-1', alfa: 'Respuesta A', beta: 'Respuesta B' }));

    await store.vote(battle.id, 'Beta');

    expect(hub.votes[0]).toEqual(['s1', 'battle-1', 'Beta']);
    expect(store.messages().at(-1)).toEqual(expect.objectContaining({ kind: 'assistant', text: 'Respuesta B', arenaWinner: 'Beta' }));
  });

  it('keeps streaming into a conversation the user left', async () => {
    hub.sessions = [
      { id: 's1', createdAt: '', preview: '', messageCount: 0 },
      { id: 's2', createdAt: '', preview: '', messageCount: 0 },
    ];
    await store.init();
    await store.send('largo');
    await store.selectSession('s2');

    hub.textStream.observer!.next('sigue');
    hub.textStream.observer!.complete();
    await store.selectSession('s1');

    expect((store.messages().at(-1) as AssistantMessage).text).toBe('sigue');
  });

  it('adds assistant messages pushed by the server', async () => {
    await store.init();

    hub.received.next('Mensaje del servidor');

    expect(store.messages().at(-1)).toEqual(expect.objectContaining({ kind: 'assistant', text: 'Mensaje del servidor' }));
  });

  it('reconnects before sending when the connection was lost', async () => {
    await store.init();
    hub.isConnected = false;
    hub.connectionChanges.next('disconnected');
    expect(store.connection()).toBe('disconnected');

    await store.send('¿Sigues ahí?');

    expect(hub.connect).toHaveBeenCalledTimes(2);
    expect(store.connection()).toBe('connected');
    expect(hub.streamMessage).toHaveBeenCalledWith('s1', '¿Sigues ahí?');
  });

  it('shows the message and a notice when the reconnection fails', async () => {
    await store.init();
    hub.isConnected = false;
    hub.connect.mockRejectedValueOnce(new Error('offline'));

    await store.send('hola');

    expect(store.connection()).toBe('disconnected');
    expect(hub.streamMessage).not.toHaveBeenCalled();
    expect(store.messages().at(-2)).toEqual(expect.objectContaining({ kind: 'user', text: 'hola' }));
    expect((store.messages().at(-1) as AssistantMessage).text).toContain(RECONNECT_FAILED_MESSAGE);
  });

  it('follows the automatic reconnection state of the hub', async () => {
    await store.init();

    hub.connectionChanges.next('reconnecting');
    expect(store.connection()).toBe('reconnecting');
    hub.connectionChanges.next('connected');
    expect(store.connection()).toBe('connected');
  });

  it('holds the inactivity timeout while an answer is streaming', async () => {
    await store.init();
    await store.send('hola');
    expect(inactivity.activeHolds).toBe(1);

    hub.textStream.observer!.complete();
    expect(inactivity.activeHolds).toBe(0);
  });

  it('releases the inactivity hold when the stream fails', async () => {
    await store.init();
    await store.send('hola');

    hub.textStream.observer!.error(new Error('boom'));
    expect(inactivity.activeHolds).toBe(0);
  });
});
