import { DestroyRef, Injectable, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { Subscription, fromEvent, merge, throttleTime } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthFacade } from './auth-facade.service';

const ACTIVITY_EVENTS = ['mousemove', 'touchstart', 'keydown'] as const;
const DEFAULT_TIMEOUT_MINUTES = 30;

/**
 * Logs the user out after `environment.sessionTimeoutMinutes` without interaction. Work that must not be cut off
 * (a chat answer being streamed) takes a hold with `hold()`; the countdown only runs while there are no holds.
 *
 * Timers are throttled or frozen while the tab is hidden or the phone is locked, so the elapsed time is also
 * checked when the page becomes visible again.
 */
@Injectable({ providedIn: 'root' })
export class InactivityService {
  private readonly auth = inject(AuthFacade);
  private readonly document = inject(DOCUMENT);

  private subscription: Subscription | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private lastActivity = Date.now();
  private holds = 0;

  readonly timeoutMs = (environment.sessionTimeoutMinutes ?? DEFAULT_TIMEOUT_MINUTES) * 60_000;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stop());
  }

  get running(): boolean {
    return this.subscription !== null;
  }

  /** Starts watching; idempotent. */
  start(): void {
    if (this.subscription) return;
    const activity$ = merge(...ACTIVITY_EVENTS.map(name => fromEvent(this.document, name, { passive: true })));
    this.subscription = activity$.pipe(throttleTime(1000)).subscribe(() => this.touch());
    this.subscription.add(fromEvent(this.document, 'visibilitychange').subscribe(() => this.onVisibilityChange()));
    this.touch();
  }

  stop(): void {
    this.subscription?.unsubscribe();
    this.subscription = null;
    this.clearTimer();
  }

  /** Pauses the countdown until the returned function is called (once). */
  hold(): () => void {
    this.holds++;
    this.clearTimer();
    let released = false;
    return () => {
      if (released) return;
      released = true;
      this.holds--;
      // Finishing the held work counts as activity: the user gets a full timeout to read the answer.
      if (this.holds === 0 && this.running) this.touch();
    };
  }

  private touch(): void {
    this.lastActivity = Date.now();
    this.schedule(this.timeoutMs);
  }

  private onVisibilityChange(): void {
    if (this.document.visibilityState !== 'visible') return;
    const remaining = this.timeoutMs - (Date.now() - this.lastActivity);
    if (remaining <= 0 && this.holds === 0) {
      void this.expire();
    } else {
      this.touch();
    }
  }

  private schedule(delay: number): void {
    this.clearTimer();
    if (this.holds > 0 || !this.running) return;
    this.timer = setTimeout(() => void this.expire(), delay);
  }

  private async expire(): Promise<void> {
    this.stop();
    await this.auth.logout();
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }
}
