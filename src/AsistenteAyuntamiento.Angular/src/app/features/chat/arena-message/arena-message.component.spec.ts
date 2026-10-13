import { TestBed } from '@angular/core/testing';
import { ArenaSide, ArenaVote } from '../models/chat.models';
import { ArenaMessageComponent } from './arena-message.component';

describe('ArenaMessageComponent', () => {
  function setup(canVote = true, failed?: ArenaSide[]) {
    const fixture = TestBed.createComponent(ArenaMessageComponent);
    fixture.componentRef.setInput('message', { kind: 'arena', id: 'm1', battleId: 'b1', alfa: '**Uno**', beta: 'Dos', failed });
    fixture.componentRef.setInput('canVote', canVote);
    const votes: ArenaVote[] = [];
    fixture.componentInstance.vote.subscribe(v => votes.push(v));
    fixture.detectChanges();
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    return { fixture, votes, buttons };
  }

  it('renders both answers as Markdown', () => {
    const { fixture } = setup();
    expect(fixture.nativeElement.querySelector('strong')?.textContent).toBe('Uno');
  });

  it('offers a labelled button for each side and a tie', () => {
    const { buttons, votes } = setup();
    const byLabel = (label: string) => buttons.find(b => b.getAttribute('aria-label') === label || b.textContent?.trim() === label)!;

    byLabel('Elegir esta respuesta (modelo Alfa)').click();
    byLabel('Empate').click();
    byLabel('Elegir esta respuesta (modelo Beta)').click();

    expect(votes).toEqual(['Alfa', 'Tie', 'Beta']);
  });

  it('disables voting while the answers are streaming', () => {
    const { buttons } = setup(false);
    expect(buttons.every(b => b.disabled)).toBe(true);
  });

  it('disables voting and says why when a side failed', () => {
    const { fixture, buttons } = setup(true, ['Beta']);
    expect(buttons.every(b => b.disabled)).toBe(true);
    expect(fixture.nativeElement.querySelector('[role="status"]')?.textContent).toContain('No se puede votar');
  });
});
