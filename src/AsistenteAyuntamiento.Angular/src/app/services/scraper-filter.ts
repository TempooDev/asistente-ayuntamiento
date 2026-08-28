import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';

export enum DocumentSource {
  BOE = 'BOE',
  BOJA = 'BOJA',
  BOPMA = 'BOPMA',
  Unknown = 'Unknown'
}

export enum FilterType {
  Department = 'Department',
  Section = 'Section',
  Keyword = 'Keyword',
  BojaFeed = 'BojaFeed'
}

export interface ScraperFilterRuleDto {
  id: number;
  provider: DocumentSource;
  filterType: FilterType;
  value: string;
  isActive: boolean;
}

export interface CreateFilterRuleDto {
  provider: DocumentSource;
  filterType: FilterType;
  value: string;
}

export interface UpdateFilterRuleStatusDto {
  isActive: boolean;
}

export interface TriggerScrapeDto {
  provider: DocumentSource;
  startDate?: string;
  endDate?: string;
}

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
    return this.http.put(`${this.apiUrl}/${id}`, dto);
  }

  deleteFilter(id: number) {
    return this.http.delete(`${this.apiUrl}/${id}`);
  }

  triggerScrape(dto: TriggerScrapeDto) {
    return this.http.post(`${this.apiUrl}/trigger`, dto);
  }
}
