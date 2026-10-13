import { EnvironmentProviders, ErrorHandler, Provider, inject, provideAppInitializer } from '@angular/core';
import { FaroErrorHandler } from './faro-error-handler';
import { FaroTelemetry } from './faro-telemetry.service';

/** Starts Faro without delaying the app (it is loaded in the background) and routes errors to it. */
export function provideTelemetry(): (Provider | EnvironmentProviders)[] {
  return [
    { provide: ErrorHandler, useClass: FaroErrorHandler },
    provideAppInitializer(() => {
      const telemetry = inject(FaroTelemetry);
      void telemetry.start().catch(err => console.warn('Telemetry could not start', err));
    }),
  ];
}
