import { ErrorHandler, Injectable, inject } from '@angular/core';
import { FaroTelemetry } from './faro-telemetry.service';

/** Keeps Angular's default logging and also reports the error to Faro. */
@Injectable()
export class FaroErrorHandler extends ErrorHandler {
  private readonly telemetry = inject(FaroTelemetry);

  override handleError(error: unknown): void {
    super.handleError(error);
    this.telemetry.pushError(error);
  }
}
