import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTestDependencies } from '../../../testing/test-providers';
import { ChatPanelComponent } from './chat-panel';

describe('ChatPanelComponent', () => {
  let fixture: ComponentFixture<ChatPanelComponent>;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [ChatPanelComponent],
      providers: provideTestDependencies(),
    }).compileComponents();

    fixture = TestBed.createComponent(ChatPanelComponent);
  });

  it('should create', () => {
    expect(fixture.componentInstance).toBeTruthy();
  });
});
