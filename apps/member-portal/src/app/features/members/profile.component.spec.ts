import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { ProfileComponent } from './profile.component';
import { MyProfile } from './members.models';

function profile(overrides: Partial<MyProfile> = {}): MyProfile {
  return {
    id: 'm1',
    tenantId: 't1',
    fullName: 'Ravi Shah',
    photoUrl: null,
    dateOfBirth: '1990-05-14',
    gender: 'Male',
    mobile: '+919812345678',
    email: 'ravi@example.com',
    address: null,
    locality: 'Udaipur',
    profession: null,
    privacy: {
      mobile: 'SamaajOnly',
      email: 'Private',
      address: 'Private',
      profession: 'SamaajOnly',
      dateOfBirth: 'Private',
    },
    isListedInDirectory: true,
    createdAt: '2026-01-01T10:00:00Z',
    updatedAt: null,
    ...overrides,
  };
}

describe('ProfileComponent', () => {
  let fixture: ComponentFixture<ProfileComponent>;
  let component: ProfileComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ProfileComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(ProfileComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(me: MyProfile = profile()): void {
    fixture.detectChanges();
    http.expectOne('/v1/members/me').flush(me);
    fixture.detectChanges();
  }

  it('fills the form from what the server holds', () => {
    load();

    expect(component.form.fullName).toBe('Ravi Shah');
    expect(component.form.locality).toBe('Udaipur');
    expect(component.form.privacy.mobile).toBe('SamaajOnly');
    expect(component.form.isListedInDirectory).toBe(true);
  });

  it('seeds every optional field to an empty string, never undefined', () => {
    // An undefined bound to a control renders as an empty one that then posts
    // undefined — the same trap as a <select> with no matching option.
    load(profile({ address: null, profession: null, photoUrl: null }));

    expect(component.form.address).toBe('');
    expect(component.form.profession).toBe('');
    expect(component.form.photoUrl).toBe('');
  });

  it('sends the directory setting on every save', () => {
    // The service refuses a body without it. Omitting it here would have made
    // every profile edit a 400 — or, if the service had defaulted, would have
    // put an opted-out member back in the directory.
    load(profile({ isListedInDirectory: false }));

    component.save();

    const request = http.expectOne('/v1/members/m1');

    expect(request.request.body.isListedInDirectory).toBe(false);

    request.flush(profile({ isListedInDirectory: false }));
  });

  it('unticking the box takes the member out of the directory', () => {
    load();

    component.form.isListedInDirectory = false;
    component.save();

    const request = http.expectOne('/v1/members/m1');

    expect(request.request.body.isListedInDirectory).toBe(false);

    request.flush(profile({ isListedInDirectory: false }));
    fixture.detectChanges();

    expect(text()).toContain('saved');
  });

  it('sends null for a field the member cleared, not an empty string', () => {
    // "Cleared" and "blank" would otherwise be two different states in one
    // column, and only one of them reads as "not shared".
    load(profile({ profession: 'Architect' }));

    component.form.profession = '   ';
    component.save();

    const request = http.expectOne('/v1/members/m1');

    expect(request.request.body.profession).toBeNull();

    request.flush(profile({ profession: null }));
  });

  it('refills from the response rather than from what was typed', () => {
    // The service trims and lowercases the email; a screen showing its own copy
    // is the one that is wrong.
    load();

    component.form.email = '  RAVI@EXAMPLE.COM  ';
    component.save();

    http.expectOne('/v1/members/m1').flush(profile({ email: 'ravi@example.com' }));
    fixture.detectChanges();

    expect(component.form.email).toBe('ravi@example.com');
  });

  it('will not save a profile with no name, and says why', () => {
    load();

    component.form.fullName = '   ';
    component.save();

    fixture.detectChanges();

    http.expectNone('/v1/members/m1');
    expect(text()).toContain('needs a name');
  });

  it('says the directory setting does not hide a member everywhere', () => {
    // A control that read as "hide me from the platform" would be promising
    // something it does not do: an unlisted profile is still reachable by id.
    load();

    expect(text()).toContain('does not hide you');
  });

  it('shows the failure rather than a blank form', () => {
    fixture.detectChanges();

    http.expectOne('/v1/members/me').flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
  });
});
