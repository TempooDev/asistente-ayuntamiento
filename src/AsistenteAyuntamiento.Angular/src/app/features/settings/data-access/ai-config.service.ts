import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { AiConfigurationDto, SaveAiConfigurationDto } from '../models/settings.models';

/** Chat model settings of the tenant. The API key is write-only: reads only say whether one is stored. */
@Injectable({ providedIn: 'root' })
export class AiConfigService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiBaseUrl}/api/settings/ai`;

  getConfiguration(): Observable<AiConfigurationDto> {
    return this.http.get<AiConfigurationDto | null>(this.url).pipe(
      map(config => config ?? { provider: 'ollama', model: 'llama3.2', temperature: 0.3, hasApiKey: false }),
    );
  }

  /** An empty apiKey keeps the stored one. */
  saveConfiguration(dto: SaveAiConfigurationDto): Observable<void> {
    const { apiKey, ...rest } = dto;
    return this.http.put<void>(this.url, apiKey ? dto : rest);
  }
}
