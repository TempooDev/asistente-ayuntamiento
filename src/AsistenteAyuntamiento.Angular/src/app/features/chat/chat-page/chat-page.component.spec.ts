import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTestDependencies } from '../../../../testing/test-providers';
import { ChatPageComponent } from './chat-page.component';

describe('ChatPageComponent', () => {
  let fixture: ComponentFixture<ChatPageComponent>;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [ChatPageComponent],
      providers: provideTestDependencies(),
    }).compileComponents();

    fixture = TestBed.createComponent(ChatPageComponent);
  });

  it('should create', () => {
    expect(fixture.componentInstance).toBeTruthy();
  });
});
