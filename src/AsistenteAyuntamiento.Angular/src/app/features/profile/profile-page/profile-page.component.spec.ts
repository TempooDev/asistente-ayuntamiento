import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTestDependencies } from '../../../../testing/test-providers';
import { ProfilePageComponent } from './profile-page.component';

describe('ProfilePageComponent', () => {
  let fixture: ComponentFixture<ProfilePageComponent>;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [ProfilePageComponent],
      providers: provideTestDependencies(),
    }).compileComponents();

    fixture = TestBed.createComponent(ProfilePageComponent);
  });

  it('should create', () => {
    expect(fixture.componentInstance).toBeTruthy();
  });
});
