import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../../environments/environment';
import { ArenaCompareRequest, ArenaCompareResponse, ArenaVoteRequest, ArenaVoteResponse } from '../models/arena.models';

@Injectable({
  providedIn: 'root',
})
export class ArenaService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiBaseUrl}/api/arena`;

  // --- UI State (Lifted to prevent loss on navigation) ---
  readonly currentQuery = signal('');
  readonly compareData = signal<ArenaCompareResponse | null>(null);
  readonly voteResult = signal<ArenaVoteResponse | null>(null);

  compare(request: ArenaCompareRequest) {
    return this.http.post<ArenaCompareResponse>(`${this.apiUrl}/compare`, request);
  }

  vote(request: ArenaVoteRequest) {
    return this.http.post<ArenaVoteResponse>(`${this.apiUrl}/vote`, request);
  }
}
