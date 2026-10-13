import { TestBed } from '@angular/core/testing';
import { FakeHubConnectionFactory, provideTestDependencies } from '../../../testing/test-providers';
import { HubConnectionFactory } from './hub-connection.factory';
import { DocumentStatusNotification, NotificationService } from './notification.service';

describe('NotificationService', () => {
  function setup() {
    TestBed.configureTestingModule({ providers: provideTestDependencies() });
    const service = TestBed.inject(NotificationService);
    const hub = (TestBed.inject(HubConnectionFactory) as unknown as FakeHubConnectionFactory).connections.get('/hubs/notifications')!;
    const received: DocumentStatusNotification[][] = [];
    service.documentStatusUpdates$.subscribe(batch => received.push(batch));
    return { hub, received };
  }

  it('delivers a DocumentStatusesChanged batch as one update', () => {
    const { hub, received } = setup();
    const batch = [
      { documentId: 'BOE-A-1', status: 'Queued' },
      { documentId: 'BOE-A-2', status: 'Pending' },
    ];

    hub.handlers.get('DocumentStatusesChanged')!(batch);

    expect(received).toEqual([batch]);
  });

  it('delivers a single DocumentStatusChanged as a batch of one', () => {
    const { hub, received } = setup();

    hub.handlers.get('DocumentStatusChanged')!({ documentId: 'BOE-A-3', status: 'Completed' });

    expect(received).toEqual([[{ documentId: 'BOE-A-3', status: 'Completed' }]]);
  });
});
