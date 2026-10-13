import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { AiConfigurationDto, SaveAiConfigurationDto } from '../models/settings.models';

@Injectable({
  providedIn: 'root'
})
export class AiConfigService {
  private http = inject(HttpClient);

  async getConfiguration(): Promise<AiConfigurationDto> {
    const config = await firstValueFrom(
      this.http.get<AiConfigurationDto>(`${environment.apiBaseUrl}/api/settings/ai`)
    );
    return config || {
      provider: 'ollama',
      model: 'llama3.2',
      temperature: 0.3,
      hasApiKey: false
    };
  }

  async saveConfiguration(dto: SaveAiConfigurationDto): Promise<void> {
    await firstValueFrom(
      this.http.put(`${environment.apiBaseUrl}/api/settings/ai`, dto)
    );
  }
}
