import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../../environments/environment';
import { FilterType, ScraperFilterRuleDto, CreateFilterRuleDto, UpdateFilterRuleStatusDto, TriggerScrapeDto, ScraperState } from '../models/settings.models';
import { DocumentSource } from '../../../shared/models/document-source';

@Injectable({
  providedIn: 'root'
})
export class ScraperFilterService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiBaseUrl}/api/scraper/filters`;

  getFilters() {
    return this.http.get<ScraperFilterRuleDto[]>(this.apiUrl);
  }

  createFilter(dto: CreateFilterRuleDto) {
    return this.http.post<ScraperFilterRuleDto>(this.apiUrl, dto);
  }

  updateFilterStatus(id: number, dto: UpdateFilterRuleStatusDto) {
    return this.http.put<void>(`${this.apiUrl}/${id}`, dto);
  }

  deleteFilter(id: number) {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  triggerScrape(dto: TriggerScrapeDto) {
    return this.http.post<void>(`${this.apiUrl}/trigger`, dto);
  }

  getState() {
    return this.http.get<ScraperState>(`${this.apiUrl}/state`);
  }
}
