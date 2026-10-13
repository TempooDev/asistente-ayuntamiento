import { TestBed } from '@angular/core/testing';
import { AuthFacade } from './auth-facade.service';
import { InactivityService } from './inactivity.service';

describe('InactivityService', () => {
  const logout = vi.fn(() => Promise.resolve());
  let service: InactivityService;

  const interact = () => document.dispatchEvent(new Event('keydown'));
  const setVisibility = (state: DocumentVisibilityState) => {
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state });
    document.dispatchEvent(new Event('visibilitychange'));
  };

  beforeEach(() => {
    vi.useFakeTimers();
    logout.mockClear();
    TestBed.configureTestingModule({ providers: [{ provide: AuthFacade, useValue: { logout } }] });
    service = TestBed.inject(InactivityService);
  });

  afterEach(() => {
    service.stop();
    vi.useRealTimers();
    setVisibility('visible');
  });

  it('logs out after the timeout without interaction', () => {
    service.start();

    vi.advanceTimersByTime(service.timeoutMs - 1);
    expect(logout).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(logout).toHaveBeenCalledTimes(1);
    expect(service.running).toBe(false);
  });

  it('restarts the countdown on interaction', () => {
    service.start();

    vi.advanceTimersByTime(service.timeoutMs - 5_000);
    interact();
    vi.advanceTimersByTime(service.timeoutMs - 5_000);

    expect(logout).not.toHaveBeenCalled();
    vi.advanceTimersByTime(5_000);
    expect(logout).toHaveBeenCalledTimes(1);
  });

  it('does not log out while a hold is active, and counts its release as activity', () => {
    service.start();
    const release = service.hold();

    vi.advanceTimersByTime(service.timeoutMs * 2);
    expect(logout).not.toHaveBeenCalled();

    release();
    release(); // Releasing twice is harmless.
    vi.advanceTimersByTime(service.timeoutMs - 1);
    expect(logout).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(logout).toHaveBeenCalledTimes(1);
  });

  it('logs out on return when the screen was locked for longer than the timeout', () => {
    service.start();
    setVisibility('hidden');

    // A frozen tab does not run its timers; only the clock moves.
    vi.setSystemTime(Date.now() + service.timeoutMs + 1_000);
    setVisibility('visible');

    expect(logout).toHaveBeenCalledTimes(1);
  });

  it('keeps the session when the user returns before the timeout', () => {
    service.start();
    setVisibility('hidden');

    vi.setSystemTime(Date.now() + 60_000);
    setVisibility('visible');

    expect(logout).not.toHaveBeenCalled();
    expect(service.running).toBe(true);
  });

  it('does nothing after stop()', () => {
    service.start();
    service.stop();

    vi.advanceTimersByTime(service.timeoutMs * 2);
    expect(logout).not.toHaveBeenCalled();
  });
});
