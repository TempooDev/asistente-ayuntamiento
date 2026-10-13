import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { FakeHubConnectionFactory, provideTestDependencies } from '../../../../testing/test-providers';
import { HubConnectionFactory } from '../../../core/realtime/hub-connection.factory';
import { ToastService } from '../../../shared/ui/toast.service';
import { ManualScrapeComponent } from './manual-scrape.component';

describe('ManualScrapeComponent', () => {
  function setup() {
    TestBed.configureTestingModule({ imports: [ManualScrapeComponent], providers: provideTestDependencies() });
    const fixture = TestBed.createComponent(ManualScrapeComponent);
    TestBed.inject(HttpTestingController).match(() => true).forEach(r => r.flush({ isScraping: false, message: '' }));
    const toast = TestBed.inject(ToastService);
    const success = vi.spyOn(toast, 'success');
    const error = vi.spyOn(toast, 'error');
    const hub = (TestBed.inject(HubConnectionFactory) as unknown as FakeHubConnectionFactory).connections.get('/hubs/notifications')!;
    const state = (isScraping: boolean, message: string) => hub.handlers.get('ScraperStateChanged')!({ isScraping, message });
    return { fixture, success, error, state };
  }

  it('reports a finished scrape as a success', () => {
    const { success, error, state } = setup();

    state(true, 'Extrayendo BOE…');
    state(false, '');

    expect(success).toHaveBeenCalledOnce();
    expect(error).not.toHaveBeenCalled();
  });

  it('shows the reason when the scrape did not complete', () => {
    const { success, error, state } = setup();

    state(true, 'Extrayendo BOE…');
    state(false, 'Ya hay un trabajo en curso.');

    expect(error).toHaveBeenCalledWith('Ya hay un trabajo en curso.');
    expect(success).not.toHaveBeenCalled();
  });
});
