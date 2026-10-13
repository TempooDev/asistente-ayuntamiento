import { DocumentSource } from '../../../shared/models/document-source';

export interface AiConfigurationDto {
  provider: string;
  model: string;
  temperature: number;
  hasApiKey: boolean;
  endpointUrl?: string;
}

export interface SaveAiConfigurationDto {
  provider: string;
  model: string;
  temperature: number;
  apiKey?: string;
  endpointUrl?: string;
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
