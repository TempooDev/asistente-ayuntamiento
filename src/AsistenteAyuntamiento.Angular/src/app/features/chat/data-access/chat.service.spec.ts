import { TestBed } from '@angular/core/testing';
import { HubConnectionFactory } from '../../../core/realtime/hub-connection.factory';
import { FakeHubConnectionFactory, provideTestDependencies } from '../../../../testing/test-providers';
import { ChatService } from './chat.service';

describe('ChatService', () => {
  let service: ChatService;
  let hubs: FakeHubConnectionFactory;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: provideTestDependencies() });
    service = TestBed.inject(ChatService);
    hubs = TestBed.inject(HubConnectionFactory) as unknown as FakeHubConnectionFactory;
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('forwards ReceiveMessage hub events to messageReceived$', () => {
    const received: string[] = [];
    service.messageReceived$.subscribe(message => received.push(message));

    hubs.connections.get('/hubs/chat')!.handlers.get('ReceiveMessage')!('hola');

    expect(received).toEqual(['hola']);
  });
});
