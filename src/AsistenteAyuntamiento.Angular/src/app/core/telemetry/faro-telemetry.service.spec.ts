import { ErrorHandler } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { ActivatedRouteSnapshot } from '@angular/router';
import type { TransportItem } from '@grafana/faro-web-sdk';
import { provideTestDependencies } from '../../../testing/test-providers';
import { FaroErrorHandler } from './faro-error-handler';
import { FaroTelemetry, TELEMETRY_CONFIG_PATH, routeTemplate, scrub, stripQuery, traceHeaderTargets } from './faro-telemetry.service';

describe('Faro telemetry helpers', () => {
  it('strips query strings and fragments (Auth0 code/state, search text)', () => {
    expect(stripQuery('https://app.example/callback?code=abc&state=xyz')).toBe('https://app.example/callback');
    expect(stripQuery('https://app.example/chat#top')).toBe('https://app.example/chat');
    expect(stripQuery('https://app.example/chat')).toBe('https://app.example/chat');
  });

  it('scrubs the page URL of every item', () => {
    const item = { meta: { page: { url: 'https://app.example/callback?code=abc' } } } as unknown as TransportItem;

    expect(scrub(item).meta.page!.url).toBe('https://app.example/callback');
  });

  it('names views by route template, never by parameter values', () => {
    const snapshot = (path: string, firstChild: ActivatedRouteSnapshot | null = null) =>
      ({ routeConfig: path === '' ? null : { path }, firstChild }) as unknown as ActivatedRouteSnapshot;

    expect(routeTemplate(snapshot('', snapshot('admin', snapshot('analytics'))))).toBe('/admin/analytics');
    expect(routeTemplate(snapshot('', snapshot('documentos', snapshot(':id'))))).toBe('/documentos/:id');
  });

  it('propagates trace headers only to the API and hubs of the API origin', () => {
    const [sameOrigin] = traceHeaderTargets('', 'https://app.example');
    expect(sameOrigin.test('https://app.example/api/chat')).toBe(true);
    expect(sameOrigin.test('https://app.example/hubs/chat/negotiate')).toBe(true);
    expect(sameOrigin.test('https://app.example/telemetry/faro')).toBe(false);
    expect(sameOrigin.test('https://tenant.eu.auth0.com/oauth/token')).toBe(false);
    expect(sameOrigin.test('https://app.example.evil.com/api/x')).toBe(false);

    const [separateApi] = traceHeaderTargets('https://api.example/', 'https://app.example');
    expect(separateApi.test('https://api.example/api/documents')).toBe(true);
    expect(separateApi.test('https://app.example/api/documents')).toBe(false);
  });
});

describe('FaroTelemetry', () => {
  let telemetry: FaroTelemetry;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [...provideTestDependencies(), { provide: ErrorHandler, useClass: FaroErrorHandler }],
    });
    telemetry = TestBed.inject(FaroTelemetry);
    http = TestBed.inject(HttpTestingController);
  });

  it('stays off when the gateway has no collector, and drops the errors it buffered', async () => {
    telemetry.pushError(new Error('before start'));

    const started = telemetry.start();
    http.expectOne(TELEMETRY_CONFIG_PATH).flush({ enabled: false, collectorUrl: null, environment: 'production', sampleRate: 1 });
    await started;

    expect(telemetry.active).toBe(false);
    expect(() => telemetry.pushError(new Error('after start'))).not.toThrow();
  });

  it('stays off when the configuration cannot be loaded', async () => {
    const started = telemetry.start();
    http.expectOne(TELEMETRY_CONFIG_PATH).flush('down', { status: 502, statusText: 'Bad Gateway' });
    await started;

    expect(telemetry.active).toBe(false);
  });

  it('routes Angular errors to Faro and keeps the default logging', () => {
    const pushError = vi.spyOn(telemetry, 'pushError');
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const error = new Error('boom');

    TestBed.inject(ErrorHandler).handleError(error);

    expect(pushError).toHaveBeenCalledWith(error);
    expect(consoleError).toHaveBeenCalled();
    consoleError.mockRestore();
  });
});
