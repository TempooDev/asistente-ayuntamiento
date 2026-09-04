import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

export interface ArenaCompareRequest {
  query: string;
}

export interface ArenaCompareResponse {
  sessionId: string;
  optionAlfa: string;
  optionBeta: string;
  latencyAlfaMs: number;
  latencyBetaMs: number;
  sourcesAlfa: string[];
  sourcesBeta: string[];
}

export interface ArenaVoteRequest {
  sessionId: string;
  winner: string; // 'ALFA', 'BETA', 'TIE', 'BOTH_BAD'
  clarityReason?: string;
  precisionReason?: string;
  optionalComment?: string;
}

export interface ArenaVoteResponse {
  alfaSystem: string;
  betaSystem: string;
}

@Injectable({
  providedIn: 'root',
})
export class ArenaApi {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiBaseUrl}/api/arena`;

  compare(request: ArenaCompareRequest) {
    return this.http.post<ArenaCompareResponse>(`${this.apiUrl}/compare`, request);
  }

  vote(request: ArenaVoteRequest) {
    return this.http.post<ArenaVoteResponse>(`${this.apiUrl}/vote`, request);
  }
}
