import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DecimalPipe } from '@angular/common';
import { AiConfigService, SaveAiConfigurationDto } from '../../services/ai-config';

@Component({
  selector: 'app-configuracion',
  standalone: true,
  imports: [FormsModule, DecimalPipe],
  templateUrl: './configuracion.html',
  styleUrl: './configuracion.scss'
})
export class ConfiguracionComponent implements OnInit {
  private aiClient = inject(AiConfigService);

  // Form Model
  config = signal<SaveAiConfigurationDto>({
    provider: 'ollama',
    model: 'llama3.2',
    temperature: 0.3
  });

  hasSavedApiKey = signal(false);
  isLoading = signal(true);
  isSaving = signal(false);
  showSuccessMessage = signal(false);
  showErrorMessage = signal(false);

  async ngOnInit() {
    await this.cargarConfiguracion();
  }

  async cargarConfiguracion() {
    this.isLoading.set(true);
    this.showSuccessMessage.set(false);
    this.showErrorMessage.set(false);

    try {
      const currentConfig = await this.aiClient.getConfiguration();
      this.config.set({
        provider: currentConfig.provider,
        model: currentConfig.model,
        temperature: currentConfig.temperature,
        endpointUrl: currentConfig.endpointUrl,
        apiKey: '' // Never show the real API key on the frontend
      });
      this.hasSavedApiKey.set(currentConfig.hasApiKey);
    } catch (e) {
      console.error(e);
      this.showErrorMessage.set(true);
    } finally {
      this.isLoading.set(false);
    }
  }

  async guardarConfiguracion() {
    this.isSaving.set(true);
    this.showSuccessMessage.set(false);
    this.showErrorMessage.set(false);

    try {
      const currentConfig = this.config();
      // Ensure empty string is sent as undefined to not wipe it if not intended,
      // or handle backend logic. If it's an empty string and not meant to be updated, delete it.
      const payload = { ...currentConfig };
      if (!payload.apiKey) {
        delete payload.apiKey;
      }
      
      await this.aiClient.saveConfiguration(payload);
      this.showSuccessMessage.set(true);

      if (payload.apiKey) {
        this.hasSavedApiKey.set(true);
        this.config.update(c => ({ ...c, apiKey: '' })); // Clear from input
      }

      setTimeout(() => {
        this.showSuccessMessage.set(false);
      }, 3000);
    } catch (e) {
      console.error(e);
      this.showErrorMessage.set(true);
    } finally {
      this.isSaving.set(false);
    }
  }

  updateConfig(key: keyof SaveAiConfigurationDto, value: any) {
    this.config.update(c => ({ ...c, [key]: value }));
  }
}
