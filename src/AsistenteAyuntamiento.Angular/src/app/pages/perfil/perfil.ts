import { Component, inject, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '@auth0/auth0-angular';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-perfil',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './perfil.html',
})
export class PerfilComponent implements OnInit {
  public auth = inject(AuthService);
  private fb = inject(FormBuilder);

  profileForm: FormGroup;
  isEditing = false;
  isSaving = false;
  saveSuccess = false;

  constructor() {
    this.profileForm = this.fb.group({
      name: ['', Validators.required],
      nickname: ['']
    });
  }

  ngOnInit() {
    this.auth.user$.subscribe(user => {
      if (user) {
        this.profileForm.patchValue({
          name: user.name || '',
          nickname: user.nickname || ''
        });
      }
    });
  }

  toggleEdit() {
    this.isEditing = !this.isEditing;
    this.saveSuccess = false;
  }

  saveProfile() {
    if (this.profileForm.invalid) return;
    
    this.isSaving = true;
    
    // Simulate API call for saving profile
    setTimeout(() => {
      this.isSaving = false;
      this.isEditing = false;
      this.saveSuccess = true;
      
      // Hide success message after 3 seconds
      setTimeout(() => this.saveSuccess = false, 3000);
    }, 800);
  }

  changePassword() {
    // In a real Auth0 app, this would trigger a password reset email
    // or redirect to the Auth0 universal login password reset flow.
    alert('Se ha enviado un correo para restablecer tu contraseña.');
  }
}
