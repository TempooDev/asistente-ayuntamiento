import { TestBed } from '@angular/core/testing';
import { ChatInputComponent } from './chat-input.component';

describe('ChatInputComponent', () => {
  function setup(connected = true, busy = false) {
    const fixture = TestBed.createComponent(ChatInputComponent);
    fixture.componentRef.setInput('connected', connected);
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

  it('does not send on Shift+Enter, blank text, while busy or disconnected', () => {
    const idle = setup();
    idle.type('línea');
    idle.press(true);
    idle.type('   ');
    idle.press();
    expect(idle.sent).toEqual([]);

    const busy = setup(true, true);
    busy.type('hola');
    busy.press();
    expect(busy.sent).toEqual([]);

    const offline = setup(false);
    offline.type('hola');
    offline.press();
    expect(offline.sent).toEqual([]);
  });
});
