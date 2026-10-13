import { DestroyRef, Injectable, inject } from '@angular/core';
import { HttpBackend, HttpClient } from '@angular/common/http';
import { ActivatedRouteSnapshot, NavigationEnd, Router } from '@angular/router';
import { filter, firstValueFrom } from 'rxjs';
import type { Faro, TransportItem } from '@grafana/faro-web-sdk';
import { environment } from '../../../environments/environment';

/** Served by the gateway at /telemetry/config; no secrets (the Collector token stays in the gateway). */
export interface FaroClientConfig {
  enabled: boolean;
  collectorUrl: string | null;
  environment: string;
  sampleRate: number;
}

export const TELEMETRY_CONFIG_PATH = '/telemetry/config';
const APP_NAME = 'asistente-ayuntamiento-web';
const MAX_EARLY_ERRORS = 20;

/** Removes the query string and fragment: they may hold the Auth0 `code`/`state` or search text. */
export function stripQuery(url: string): string {
  const cut = url.search(/[?#]/);
  return cut === -1 ? url : url.slice(0, cut);
}

/** Route template of the current navigation, e.g. `/admin/analytics` (never ids or parameters). */
export function routeTemplate(root: ActivatedRouteSnapshot): string {
  const segments: string[] = [];
  for (let route: ActivatedRouteSnapshot | null = root; route; route = route.firstChild) {
    const path = route.routeConfig?.path;
    if (path) segments.push(path);
  }
  return '/' + segments.join('/');
}

/** Requests that get a `traceparent` header: only our API and hubs, on the API origin. */
export function traceHeaderTargets(apiBaseUrl: string, origin: string): RegExp[] {
  const base = (apiBaseUrl || origin).replace(/\/$/, '').replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return [new RegExp(`^${base}/(api|hubs)/`)];
}

/** Scrubs every item before it leaves the browser. */
export function scrub(item: TransportItem): TransportItem {
  const page = item.meta.page;
  if (page?.url) page.url = stripQuery(page.url);
  return item;
}

/**
 * Grafana Faro: errors, web vitals, sessions, views and browser → API traces. Loaded lazily after the app starts
 * (it stays out of the initial bundle) and only when the gateway has a Collector configured. Captures no console
 * output, user data or URLs with query strings.
 */
@Injectable({ providedIn: 'root' })
export class FaroTelemetry {
  private readonly router = inject(Router);
  private readonly http = new HttpClient(inject(HttpBackend));
  private faro: Faro | null = null;
  private earlyErrors: unknown[] = [];
  private disabled = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.faro?.pause());
  }

  get active(): boolean {
    return this.faro !== null;
  }

  async start(): Promise<void> {
    let config: FaroClientConfig;
    try {
      config = await firstValueFrom(this.http.get<FaroClientConfig>(TELEMETRY_CONFIG_PATH));
    } catch {
      config = { enabled: false, collectorUrl: null, environment: '', sampleRate: 0 };
    }
    if (!config.enabled || !config.collectorUrl) {
      this.disabled = true;
      this.earlyErrors = [];
      return;
    }

    // Absolute: Faro excludes its own transport URL from tracing only when it matches the requests it sees,
    // otherwise every send is traced and sent again.
    const collectorUrl = new URL(config.collectorUrl, window.location.origin).href;
    const [sdk, tracing] = await Promise.all([import('@grafana/faro-web-sdk'), import('@grafana/faro-web-tracing')]);
    this.faro = sdk.initializeFaro({
      url: collectorUrl,
      app: { name: APP_NAME, environment: config.environment },
      // Errors come from Angular's ErrorHandler (FaroErrorHandler), which also receives window errors through
      // provideBrowserGlobalErrorListeners; Faro's own error and console instrumentations would duplicate them.
      instrumentations: [
        new sdk.SessionInstrumentation(),
        new sdk.ViewInstrumentation(),
        new sdk.WebVitalsInstrumentation(),
        new tracing.TracingInstrumentation({
          omitTraceContextForUnsampledSessions: true,
          instrumentationOptions: {
            propagateTraceHeaderCorsUrls: traceHeaderTargets(environment.apiBaseUrl, window.location.origin),
          },
        }),
      ],
      // Faro's own sends; SignalR negotiation (it carries tokens and is not a user action); Auth0.
      ignoreUrls: [collectorUrl, /\/telemetry\//, /\/hubs\//, /auth0\.com/],
      sessionTracking: { enabled: true, persistent: false, samplingRate: config.sampleRate },
      trackGeolocation: false,
      beforeSend: scrub,
    });

    this.router.events.pipe(filter(event => event instanceof NavigationEnd)).subscribe(() => this.setView());
    this.setView();
    for (const error of this.earlyErrors) this.pushError(error);
    this.earlyErrors = [];
  }

  pushError(error: unknown): void {
    if (this.faro) {
      this.faro.api.pushError(error instanceof Error ? error : new Error(String(error)));
    } else if (!this.disabled && this.earlyErrors.length < MAX_EARLY_ERRORS) {
      this.earlyErrors.push(error); // Faro is still loading.
    }
  }

  private setView(): void {
    this.faro?.api.setView({ name: routeTemplate(this.router.routerState.snapshot.root) });
  }
}
