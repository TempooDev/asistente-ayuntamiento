import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators, FormsModule } from '@angular/forms';
import { AuthService } from '@auth0/auth0-angular';
import { CommonModule } from '@angular/common';
import { UserService } from '../data-access/user.service';
import { UserProfileDto } from '../models/profile.models';
import { UserPreferencesService } from '../data-access/user-preferences.service';
import { UserPreferenceDto } from '../models/profile.models';
import { finalize, switchMap, catchError } from 'rxjs/operators';
import { forkJoin, of } from 'rxjs';
import { AuthFacade } from '../../../core/auth/auth-facade.service';
import { ToastService } from '../../../shared/ui/toast.service';

@Component({
  selector: 'app-profile-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, FormsModule],
  templateUrl: './profile-page.component.html',
})
export class ProfilePageComponent implements OnInit {
  public auth = inject(AuthService);
  private fb = inject(FormBuilder);
  private userService = inject(UserService);
  private prefsService = inject(UserPreferencesService);
  private authFacade = inject(AuthFacade);
  private toast = inject(ToastService);

  profileForm: FormGroup;
  isEditing = signal(false);
  isSaving = signal(false);
  isLoading = signal(true);
  saveSuccess = signal(false);
  saveError = signal(false);

  // Preferences
  topics = signal<string[]>([]);
  locations = signal<string[]>([]);
  newTopic = signal('');
  newLocation = signal('');
  isAnalyzing = signal(false);

  constructor() {
    this.profileForm = this.fb.group({
      fullName: ['', Validators.required],
      department: [''],
      position: [''],
      phoneNumber: ['']
    });
  }

  ngOnInit() {
    this.loadProfile();
  }

  loadProfile() {
    this.isLoading.set(true);
    
    this.auth.user$.pipe(
      switchMap(authUser => {
        if (!authUser) return of(null);
        
        this.profileForm.patchValue({ fullName: authUser.name || '' });
        
        return forkJoin({
          authUser: of(authUser),
          profile: this.userService.getProfile().pipe(catchError(() => of(null))),
          prefs: this.prefsService.getPreferences().pipe(catchError(() => of(null)))
        });
      }),
      finalize(() => this.isLoading.set(false))
    ).subscribe({
      next: (res) => {
        if (res && res.profile) {
          this.profileForm.patchValue({
            fullName: res.profile.fullName || res.authUser.name || '',
            department: res.profile.department || '',
            position: res.profile.position || '',
            phoneNumber: res.profile.phoneNumber || ''
          });
        }
        if (res && res.prefs) {
          this.topics.set(res.prefs.topics || []);
          this.locations.set(res.prefs.locations || []);
        }
      },
      error: (err) => console.error('Error loading profile data', err)
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
    this.prefsService.analyzeHistory().subscribe({
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
    
    const profileData: UserProfileDto = this.profileForm.value;
    
    this.userService.updateProfile(profileData).pipe(
      switchMap(updatedProfile => {
        this.profileForm.patchValue(updatedProfile);
        return this.prefsService.updatePreferences({ topics: this.topics(), locations: this.locations() });
      }),
      finalize(() => this.isSaving.set(false))
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
    this.authFacade.requestPasswordReset(email).subscribe({
      next: () => this.toast.success('Se ha enviado un correo para restablecer tu contraseña. Revisa tu bandeja de entrada.'),
      error: () => this.toast.error('No se ha podido restablecer la contraseña. Si inicias sesión con Google u otra red social, cámbiala en ese proveedor.'),
    });
  }
}
