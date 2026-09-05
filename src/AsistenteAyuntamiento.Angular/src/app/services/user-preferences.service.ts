import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface UserPreferenceDto {
  topics: string[];
  locations: string[];
}

@Injectable({
  providedIn: 'root'
})
export class UserPreferencesService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiBaseUrl}/api/users/me/preferences`;

  getPreferences(): Observable<UserPreferenceDto> {
    return this.http.get<UserPreferenceDto>(this.apiUrl);
  }

  updatePreferences(preferences: UserPreferenceDto): Observable<UserPreferenceDto> {
    return this.http.put<UserPreferenceDto>(this.apiUrl, preferences);
  }

  analyzeHistory(): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/analyze`, {});
  }
}
