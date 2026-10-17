import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTestDependencies } from '../../../../testing/test-providers';
import { ChatStore } from '../data-access/chat.store';
import { ChatPageComponent } from './chat-page.component';

describe('ChatPageComponent', () => {
  let fixture: ComponentFixture<ChatPageComponent>;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [ChatPageComponent],
      providers: [...provideTestDependencies(), { provide: ChatStore, useValue: fakeStore() }],
    }).compileComponents();

    fixture = TestBed.createComponent(ChatPageComponent);
  });

  it('should create', () => {
    expect(fixture.componentInstance).toBeTruthy();
  });

  describe('mobile conversation list', () => {
    const el = () => fixture.nativeElement as HTMLElement;
    const menu = () => el().querySelector<HTMLButtonElement>('[aria-controls="chat-sessions"]')!;
    const nav = () => el().querySelector<HTMLElement>('#chat-sessions')!;

    beforeEach(async () => {
      document.body.appendChild(el());
      fixture.detectChanges();
      await fixture.whenStable();
    });
    afterEach(() => el().remove());

    it('is hidden and collapsed until the menu button opens it', () => {
      expect(menu().getAttribute('aria-expanded')).toBe('false');
      expect(nav().classList).toContain('max-md:invisible');
    });

    it('moves focus into the list when it opens', async () => {
      menu().click();
      fixture.detectChanges();
      await fixture.whenStable();

      expect(menu().getAttribute('aria-expanded')).toBe('true');
      expect(nav().classList).not.toContain('max-md:invisible');
      expect(nav().contains(document.activeElement)).toBe(true);
    });

    it('closes on Escape and gives focus back to the menu button', async () => {
      menu().click();
      fixture.detectChanges();
      await fixture.whenStable();

      nav().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      fixture.detectChanges();

      expect(menu().getAttribute('aria-expanded')).toBe('false');
      expect(document.activeElement).toBe(menu());
    });
  });
});

/** Just enough of the store for the page to render an empty conversation list. */
function fakeStore(): Partial<ChatStore> {
  const done = () => Promise.resolve();
  return {
    sessions: signal([]),
    currentSessionId: signal(''),
    messages: signal([]),
    arenaMode: signal(false),
    loadingHistory: signal(false),
    isWaiting: signal(false),
    isGenerating: signal(false),
    connection: signal('connected'),
    init: done,
  } as unknown as Partial<ChatStore>;
}
