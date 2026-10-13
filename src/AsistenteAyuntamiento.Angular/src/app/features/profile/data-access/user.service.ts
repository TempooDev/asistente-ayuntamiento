import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../../environments/environment';
import { UserProfileDto } from '../models/profile.models';

@Injectable({
  providedIn: 'root'
})
export class UserService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiBaseUrl}/api/users`;

  getProfile() {
    return this.http.get<UserProfileDto>(`${this.apiUrl}/me`);
  }

  updateProfile(profile: UserProfileDto) {
    return this.http.put<UserProfileDto>(`${this.apiUrl}/me`, profile);
  }
}
