import { TestBed } from '@angular/core/testing';
import { ChatMessage } from '../models/chat.models';
import { MessageListComponent, WAITING_MESSAGES, WAITING_MESSAGE_INTERVAL_MS } from './message-list.component';

const user = (id: string, text = 'pregunta'): ChatMessage => ({ kind: 'user', id, text });
const assistant = (id: string, text = 'respuesta'): ChatMessage => ({ kind: 'assistant', id, text });

describe('MessageListComponent', () => {
  function setup(messages: readonly ChatMessage[] = [user('u1'), assistant('a1')]) {
    const fixture = TestBed.createComponent(MessageListComponent);
    fixture.componentRef.setInput('messages', messages);
    fixture.detectChanges();
    const scroller: HTMLElement = fixture.nativeElement.querySelector('[aria-live]');
    // jsdom has no layout: a 1000px tall list in a 400px viewport.
    Object.defineProperty(scroller, 'scrollHeight', { configurable: true, get: () => 1000 });
    Object.defineProperty(scroller, 'clientHeight', { configurable: true, get: () => 400 });
    const update = async (next: readonly ChatMessage[]) => {
      fixture.componentRef.setInput('messages', next);
      fixture.detectChanges();
      await Promise.resolve(); // The scroll is applied in a microtask.
    };
    const scrollTo = async (top: number) => {
      await Promise.resolve(); // Let the initial scroll to the bottom happen first.
      scroller.scrollTop = top;
      scroller.dispatchEvent(new Event('scroll'));
    };
    return { fixture, scroller, update, scrollTo };
  }

  it('announces who wrote each message, not only by colour and position', () => {
    const { fixture } = setup([user('u1', '¿Plazo?'), assistant('a1', 'Diez días.')]);

    const bubbles: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.chat-bubble'));

    expect(bubbles.map(b => b.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['Tú: ¿Plazo?', 'Asistente: Diez días.']);
  });

  describe('waiting text', () => {
    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    it('rotates while waiting and resets when the first chunk arrives', () => {
      const { fixture } = setup();
      const text = () => fixture.nativeElement.querySelector('[data-testid="waiting-text"]')?.textContent?.trim();

      fixture.componentRef.setInput('waiting', true);
      fixture.detectChanges();
      expect(text()).toBe(WAITING_MESSAGES[0]);

      vi.advanceTimersByTime(WAITING_MESSAGE_INTERVAL_MS);
      fixture.detectChanges();
      expect(text()).toBe(WAITING_MESSAGES[1]);

      vi.advanceTimersByTime(WAITING_MESSAGE_INTERVAL_MS * 2);
      fixture.detectChanges();
      expect(text()).toBe(WAITING_MESSAGES[0]);

      fixture.componentRef.setInput('waiting', false);
      fixture.detectChanges();
      expect(text()).toBeUndefined();
      expect(vi.getTimerCount()).toBe(0);
    });
  });

  describe('scrolling', () => {
    it('follows streamed text while the user is at the bottom', async () => {
      const { scroller, update, scrollTo } = setup();
      await scrollTo(560); // 1000 - 560 - 400 = 40px from the bottom.

      await update([user('u1'), assistant('a1', 'respuesta más larga')]);

      expect(scroller.scrollTop).toBe(1000);
    });

    it('does not jump while the user reads further up', async () => {
      const { scroller, update, scrollTo } = setup();
      await scrollTo(100);

      await update([user('u1'), assistant('a1', 'respuesta más larga')]);

      expect(scroller.scrollTop).toBe(100);
    });

    it('jumps to the bottom when the user sends a message', async () => {
      const { scroller, update, scrollTo } = setup();
      await scrollTo(100);

      await update([user('u1'), assistant('a1'), user('u2'), assistant('a2', '')]);

      expect(scroller.scrollTop).toBe(1000);
    });

    it('jumps to the bottom when another conversation is opened', async () => {
      const { scroller, update, scrollTo } = setup();
      await scrollTo(100);

      await update([user('x1'), assistant('x2')]);

      expect(scroller.scrollTop).toBe(1000);
    });
  });
});
