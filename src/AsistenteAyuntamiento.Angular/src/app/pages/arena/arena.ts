import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ArenaService, ArenaCompareResponse, ArenaVoteRequest, ArenaVoteResponse } from '../../services/arena/arena.service';

@Component({
  selector: 'app-arena',
  imports: [CommonModule, FormsModule],
  templateUrl: './arena.html',
  styleUrl: './arena.scss',
})
export class Arena {
  private arenaApi = inject(ArenaService);

  // Link to service state
  query = this.arenaApi.currentQuery;
  compareData = this.arenaApi.compareData;
  voteResult = this.arenaApi.voteResult;
  
  loading = signal(false);
  error = signal('');
  
  // Voting Form (we can keep these local since they only matter while voting)
  selectedWinner = signal<string | null>(null);
  clarityReason = signal<string>('');
  precisionReason = signal<string>('');
  optionalComment = signal<string>('');
  
  voteLoading = signal(false);

  async onCompare() {
    if (!this.query().trim()) return;
    
    this.loading.set(true);
    this.error.set('');
    this.compareData.set(null);
    this.voteResult.set(null);
    this.selectedWinner.set(null);
    this.clarityReason.set('');
    this.precisionReason.set('');
    this.optionalComment.set('');

    this.arenaApi.compare({ query: this.query() }).subscribe({
        next: (res) => {
          this.compareData.set(res);
          this.loading.set(false);
        },
        error: (err) => {
          this.error.set('Error al comparar respuestas.');
          this.loading.set(false);
          console.error(err);
        }
      });
  }

  onVote() {
    const data = this.compareData();
    const winner = this.selectedWinner();
    
    if (!data || !winner) return;

    this.voteLoading.set(true);
    
    const request: ArenaVoteRequest = {
      sessionId: data.sessionId,
      winner: winner,
      clarityReason: this.clarityReason(),
      precisionReason: this.precisionReason(),
      optionalComment: this.optionalComment()
    };

    this.arenaApi.vote(request).subscribe({
      next: (res) => {
        this.voteResult.set(res);
        this.voteLoading.set(false);
      },
      error: (err) => {
        this.error.set('Error al enviar el voto.');
        this.voteLoading.set(false);
        console.error(err);
      }
    });
  }

  setWinner(winner: string) {
    this.selectedWinner.set(winner);
  }
}

