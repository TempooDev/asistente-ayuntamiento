import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators, FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, finalize, forkJoin, of, switchMap } from 'rxjs';
import { AuthFacade } from '../../../core/auth/auth-facade.service';
import { ToastService } from '../../../shared/ui/toast.service';
import { UserPreferencesService } from '../data-access/user-preferences.service';
import { UserService } from '../data-access/user.service';

@Component({
  selector: 'app-profile-page',
  imports: [ReactiveFormsModule, FormsModule],
  templateUrl: './profile-page.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfilePageComponent implements OnInit {
  protected readonly auth = inject(AuthFacade);
  private readonly userService = inject(UserService);
  private readonly prefsService = inject(UserPreferencesService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly profileForm = inject(NonNullableFormBuilder).group({
    fullName: ['', Validators.required],
    department: [''],
    position: [''],
    phoneNumber: [''],
  });
  readonly isEditing = signal(false);
  readonly isSaving = signal(false);
  readonly isLoading = signal(true);
  readonly saveSuccess = signal(false);
  readonly saveError = signal(false);

  // Preferences
  readonly topics = signal<string[]>([]);
  readonly locations = signal<string[]>([]);
  readonly newTopic = signal('');
  readonly newLocation = signal('');
  readonly isAnalyzing = signal(false);

  ngOnInit() {
    this.loadProfile();
  }

  /** Loads the profile and preferences once (not on every token refresh of the user stream). */
  loadProfile() {
    this.isLoading.set(true);
    const user = this.auth.user();
    if (!user) {
      this.isLoading.set(false);
      return;
    }
    this.profileForm.patchValue({ fullName: user.name ?? '' });

    forkJoin({
      profile: this.userService.getProfile().pipe(catchError(() => of(null))),
      prefs: this.prefsService.getPreferences().pipe(catchError(() => of(null))),
    })
      .pipe(
        finalize(() => this.isLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ profile, prefs }) => {
        if (profile) {
          this.profileForm.patchValue({
            fullName: profile.fullName || user.name || '',
            department: profile.department || '',
            position: profile.position || '',
            phoneNumber: profile.phoneNumber || '',
          });
        }
        if (prefs) {
          this.topics.set(prefs.topics ?? []);
          this.locations.set(prefs.locations ?? []);
        }
      });
  }

  addTopic() {
    const val = this.newTopic().trim();
    if (val && !this.topics().includes(val)) {
      this.topics.update(t => [...t, val]);
      this.newTopic.set('');
    }
  }
  removeTopic(topic: string) {
    this.topics.update(t => t.filter(x => x !== topic));
  }
  
  addLocation() {
    const val = this.newLocation().trim();
    if (val && !this.locations().includes(val)) {
      this.locations.update(l => [...l, val]);
      this.newLocation.set('');
    }
  }
  removeLocation(loc: string) {
    this.locations.update(l => l.filter(x => x !== loc));
  }

  analyzeHistory() {
    this.isAnalyzing.set(true);
    this.prefsService.analyzeHistory().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.toast.info('Se ha iniciado el análisis. Recarga la página en unos segundos.');
        this.isAnalyzing.set(false);
      },
      error: () => this.isAnalyzing.set(false)
    });
  }

  toggleEdit() {
    this.isEditing.set(!this.isEditing());
    this.saveSuccess.set(false);
    this.saveError.set(false);
    if (!this.isEditing()) {
      // Revert changes if cancelled
      this.loadProfile();
    }
  }

  saveProfile() {
    if (this.profileForm.invalid) return;
    
    this.isSaving.set(true);
    this.saveSuccess.set(false);
    this.saveError.set(false);
    
    this.userService.updateProfile(this.profileForm.getRawValue()).pipe(
      switchMap(updatedProfile => {
        this.profileForm.patchValue(updatedProfile);
        return this.prefsService.updatePreferences({ topics: this.topics(), locations: this.locations() });
      }),
      finalize(() => this.isSaving.set(false)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.isEditing.set(false);
        this.saveSuccess.set(true);
        setTimeout(() => this.saveSuccess.set(false), 3000);
      },
      error: (err) => {
        console.error('Error saving profile or prefs', err);
        this.saveError.set(true);
      }
    });
  }

  changePassword(email: string | undefined) {
    if (!email) return;
    this.auth.requestPasswordReset(email).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.toast.success('Se ha enviado un correo para restablecer tu contraseña. Revisa tu bandeja de entrada.'),
      error: () => this.toast.error('No se ha podido restablecer la contraseña. Si inicias sesión con Google u otra red social, cámbiala en ese proveedor.'),
    });
  }
}
