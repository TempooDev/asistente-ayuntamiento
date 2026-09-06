import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators, FormsModule } from '@angular/forms';
import { AuthService } from '@auth0/auth0-angular';
import { CommonModule } from '@angular/common';
import { UserService, UserProfileDto } from '../../services/auth/user.service';
import { UserPreferencesService, UserPreferenceDto } from '../../services/user-preferences.service';
import { finalize, switchMap, catchError } from 'rxjs/operators';
import { forkJoin, of } from 'rxjs';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-perfil',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, FormsModule],
  templateUrl: './perfil.html',
})
export class PerfilComponent implements OnInit {
  public auth = inject(AuthService);
  private fb = inject(FormBuilder);
  private userService = inject(UserService);
  private prefsService = inject(UserPreferencesService);

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
        alert('Se ha iniciado el análisis. Recarga la página en unos segundos.');
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

    // Use HttpClient directly here or create a method in UserService
    const domain = environment.auth0.domain;
    const clientId = environment.auth0.clientId;
    
    // Auth0 Authentication API endpoint for password reset
    const url = `https://${domain}/dbconnections/change_password`;
    
    const payload = {
      client_id: clientId,
      email: email,
      connection: 'Username-Password-Authentication'
    };

    // We can use fetch or HttpClient. Since we have HttpClient in user.service,
    // let's just use fetch for simplicity here, or inject HttpClient.
    fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    })
    .then(res => {
      if (res.ok) {
        alert('Se ha enviado un correo para restablecer tu contraseña. Revisa tu bandeja de entrada.');
      } else {
        alert('Hubo un problema al intentar restablecer la contraseña. Si usas inicio de sesión social (Google, etc.), no puedes cambiar la contraseña aquí.');
      }
    })
    .catch(err => {
      console.error('Error resetting password', err);
      alert('Error al contactar con el servidor de autenticación.');
    });
  }
}
