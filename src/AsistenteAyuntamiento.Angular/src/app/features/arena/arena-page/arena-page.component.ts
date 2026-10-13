import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ArenaService } from '../data-access/arena.service';
import { ArenaCompareResponse, ArenaVoteRequest, ArenaVoteResponse } from '../models/arena.models';

@Component({
  selector: 'app-arena-page',
  imports: [CommonModule, FormsModule],
  templateUrl: './arena-page.component.html',
})
export class ArenaPageComponent {
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

  loadingMessage = signal('Analizando la consulta...');
  private loadingMessages = [
    'Analizando la consulta...',
    'Buscando en normativas locales y ordenanzas...',
    'Aplicando Búsqueda Jerárquica...',
    'Sintetizando información...',
    'Redactando respuesta en lenguaje claro...',
    'Casi listo, comparando resultados...'
  ];
  private messageInterval: any;

  async onCompare() {
    if (!this.query().trim()) return;
    
    this.loading.set(true);
    
    let msgIndex = 0;
    this.loadingMessage.set(this.loadingMessages[0]);
    this.messageInterval = setInterval(() => {
      msgIndex++;
      if (msgIndex >= this.loadingMessages.length) {
        msgIndex = this.loadingMessages.length - 1; // Stay on the last message
        clearInterval(this.messageInterval);
      }
      this.loadingMessage.set(this.loadingMessages[msgIndex]);
    }, 2000);

    this.error.set('');
    this.compareData.set(null);
    this.voteResult.set(null);
    this.selectedWinner.set(null);
    this.clarityReason.set('');
    this.precisionReason.set('');
    this.optionalComment.set('');

    this.arenaApi.compare({ query: this.query() }).subscribe({
        next: (res) => {
          clearInterval(this.messageInterval);
          this.compareData.set(res);
          this.loading.set(false);
        },
        error: (err) => {
          clearInterval(this.messageInterval);
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

