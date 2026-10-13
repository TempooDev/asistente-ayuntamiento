import { TestBed } from '@angular/core/testing';
import { ConnectionState } from '../models/chat.models';
import { ChatInputComponent } from './chat-input.component';

describe('ChatInputComponent', () => {
  function setup(connection: ConnectionState = 'connected', busy = false) {
    const fixture = TestBed.createComponent(ChatInputComponent);
    fixture.componentRef.setInput('connection', connection);
    fixture.componentRef.setInput('busy', busy);
    const sent: string[] = [];
    fixture.componentInstance.send.subscribe(text => sent.push(text));
    fixture.detectChanges();
    const box: HTMLTextAreaElement = fixture.nativeElement.querySelector('textarea');
    const type = (text: string) => {
      box.value = text;
      box.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };
    const press = (shiftKey = false) => {
      box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', shiftKey }));
      fixture.detectChanges();
    };
    return { fixture, sent, type, press, box };
  }

  it('sends on Enter and clears the box', () => {
    const { sent, type, press, box } = setup();
    type('¿Horario del registro?');
    press();

    expect(sent).toEqual(['¿Horario del registro?']);
    expect(box.value).toBe('');
  });

  it('does not send on Shift+Enter, blank text, while busy or while (re)connecting', () => {
    const idle = setup();
    idle.type('línea');
    idle.press(true);
    idle.type('   ');
    idle.press();
    expect(idle.sent).toEqual([]);

    const busy = setup('connected', true);
    busy.type('hola');
    busy.press();
    expect(busy.sent).toEqual([]);

    const reconnecting = setup('reconnecting');
    reconnecting.type('hola');
    reconnecting.press();
    expect(reconnecting.sent).toEqual([]);
    expect(reconnecting.fixture.nativeElement.textContent).toContain('Reconectando...');
  });

  it('lets the user send while disconnected, so the store can reconnect', () => {
    const offline = setup('disconnected');
    expect(offline.fixture.nativeElement.textContent).toContain('Sin conexión');

    offline.type('hola');
    offline.press();
    expect(offline.sent).toEqual(['hola']);
  });
});
