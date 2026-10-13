import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ToastService } from '../../../shared/ui/toast.service';
import { AiConfigService } from '../data-access/ai-config.service';

/** Model, endpoint, API key and temperature of the tenant's chat model. */
@Component({
  selector: 'app-ai-config-form',
  imports: [ReactiveFormsModule, DecimalPipe],
  templateUrl: './ai-config-form.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AiConfigFormComponent implements OnInit {
  private readonly api = inject(AiConfigService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly form = inject(NonNullableFormBuilder).group({
    provider: ['ollama', Validators.required],
    model: ['llama3.2', Validators.required],
    endpointUrl: [''],
    apiKey: [''],
    temperature: [0.3, [Validators.min(0), Validators.max(1)]],
  });
  protected readonly temperature = toSignal(this.form.controls.temperature.valueChanges, { initialValue: 0.3 });
  protected readonly hasSavedApiKey = signal(false);
  protected readonly isLoading = signal(true);
  protected readonly isSaving = signal(false);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.api
      .getConfiguration()
      .pipe(
        finalize(() => this.isLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: config => {
          // The stored API key is never sent to the browser.
          this.form.reset({ provider: config.provider, model: config.model, endpointUrl: config.endpointUrl ?? '', apiKey: '', temperature: config.temperature });
          this.hasSavedApiKey.set(config.hasApiKey);
        },
        error: () => this.toast.error('No se ha podido cargar la configuración de IA.'),
      });
  }

  protected save(): void {
    if (this.form.invalid) return;
    this.isSaving.set(true);
    const value = this.form.getRawValue();
    this.api
      .saveConfiguration({ ...value, endpointUrl: value.endpointUrl || undefined })
      .pipe(
        finalize(() => this.isSaving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          if (value.apiKey) {
            this.hasSavedApiKey.set(true);
            this.form.controls.apiKey.reset('');
          }
          this.toast.success('Configuración guardada correctamente.');
        },
        error: () => this.toast.error('Hubo un error al guardar la configuración.'),
      });
  }
}
