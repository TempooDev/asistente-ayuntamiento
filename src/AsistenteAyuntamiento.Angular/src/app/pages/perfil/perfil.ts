import { Component, inject, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '@auth0/auth0-angular';
import { CommonModule } from '@angular/common';
import { UserService, UserProfileDto } from '../../services/user';
import { finalize } from 'rxjs/operators';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-perfil',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './perfil.html',
})
export class PerfilComponent implements OnInit {
  public auth = inject(AuthService);
  private fb = inject(FormBuilder);
  private userService = inject(UserService);

  profileForm: FormGroup;
  isEditing = false;
  isSaving = false;
  isLoading = true;
  saveSuccess = false;
  saveError = false;

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
    this.isLoading = true;
    this.userService.getProfile()
      .pipe(finalize(() => this.isLoading = false))
      .subscribe({
        next: (profile) => {
          this.profileForm.patchValue({
            fullName: profile.fullName || '',
            department: profile.department || '',
            position: profile.position || '',
            phoneNumber: profile.phoneNumber || ''
          });
        },
        error: (err) => {
          console.error('Error loading profile', err);
        }
      });
  }

  toggleEdit() {
    this.isEditing = !this.isEditing;
    this.saveSuccess = false;
    this.saveError = false;
    if (!this.isEditing) {
      // Revert changes if cancelled
      this.loadProfile();
    }
  }

  saveProfile() {
    if (this.profileForm.invalid) return;
    
    this.isSaving = true;
    this.saveSuccess = false;
    this.saveError = false;
    
    const profileData: UserProfileDto = this.profileForm.value;
    
    this.userService.updateProfile(profileData)
      .pipe(finalize(() => this.isSaving = false))
      .subscribe({
        next: (updatedProfile) => {
          this.isEditing = false;
          this.saveSuccess = true;
          this.profileForm.patchValue(updatedProfile);
          
          setTimeout(() => this.saveSuccess = false, 3000);
        },
        error: (err) => {
          console.error('Error saving profile', err);
          this.saveError = true;
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
