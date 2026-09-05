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

    // jsdom implements neither, and the directive that renders a photo uses
    // both. Without these the first profile carrying a photo throws.
    URL.createObjectURL = () => 'blob:test';
    URL.revokeObjectURL = () => undefined;

    fixture = TestBed.createComponent(ProfileComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  /**
   * Flushes the photo request the `scAuthedSrc` directive makes.
   *
   * A profile with a photo now costs a second request: the image is fetched
   * through `HttpClient` so the auth interceptor can attach the token, because
   * a plain `<img src>` would be fetched by the browser with no Authorization
   * header at all. Every test that renders a profile with a photo has to settle
   * it, or `http.verify()` fails in `afterEach` and leaves the TestBed dirty for
   * everything after it — which is exactly what happened when these tests were
   * first written.
   */
  function settlePhoto(): void {
    for (const request of http.match((r) => r.url.endsWith('/photo') && r.method === 'GET')) {
      request.flush(new Blob(['x']));
    }

    fixture.detectChanges();
  }

  function load(me: MyProfile = profile()): void {
    fixture.detectChanges();
    http.expectOne('/v1/members/me').flush(me);
    fixture.detectChanges();
    settlePhoto();
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
    load(profile({ address: null, profession: null, mobile: null }));

    expect(component.form.address).toBe('');
    expect(component.form.profession).toBe('');
    expect(component.form.mobile).toBe('');
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

  // ---- The photo ----------------------------------------------------------

  /**
   * The photo used to be a text field holding somebody else's URL. What
   * replaced it is a file input and its own request, because a file and a form
   * field are different things.
   */
  it('offers a file input rather than a link field', () => {
    load();

    const input: HTMLInputElement =
      fixture.nativeElement.querySelector('input#photo');

    expect(input.type).toBe('file');
    expect(input.accept).toBe('image/jpeg,image/png,image/webp');
  });

  it('says so plainly when the member has no photo', () => {
    load(profile({ photoUrl: null }));

    expect(text()).toContain('You have not added a photo');
  });

  function choose(file: File): void {
    const input: HTMLInputElement =
      fixture.nativeElement.querySelector('input#photo');

    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function image(bytes = 10, type = 'image/png'): File {
    return new File([new Uint8Array(bytes)], 'photo.png', { type });
  }

  it('uploads the chosen file as multipart and re-reads the profile', () => {
    load(profile({ photoUrl: null }));

    choose(image());

    const upload = http.expectOne('/v1/members/m1/photo');
    expect(upload.request.method).toBe('POST');
    expect(upload.request.body).toBeInstanceOf(FormData);

    // The browser writes the multipart boundary, so the app must not set a
    // Content-Type of its own - one without a boundary is a body no server can
    // parse.
    expect(upload.request.headers.has('Content-Type')).toBe(false);

    upload.flush(null);

    // Re-read rather than assumed: the path is the server's to derive.
    http.expectOne('/v1/members/me').flush(profile({ photoUrl: '/v1/members/m1/photo' }));
    fixture.detectChanges();
    settlePhoto();

    expect(component.photoPath()).toBe('/v1/members/m1/photo');
    expect(text()).toContain('Your photo has been updated');
  });

  /**
   * The service refuses it anyway. Checking here saves sending two megabytes
   * over a phone connection to be told what was already knowable.
   */
  it('refuses a file over 2 MB without sending it', () => {
    load();

    choose(image(2 * 1024 * 1024 + 1));

    http.verify();
    expect(text()).toContain('larger than 2 MB');
  });

  /**
   * Without clearing the input, choosing the same file twice fires no change
   * event - so a member whose first attempt failed could not retry with the
   * same picture.
   */
  it('clears the input so the same file can be chosen again', () => {
    load();

    const input: HTMLInputElement =
      fixture.nativeElement.querySelector('input#photo');

    choose(image());
    http.expectOne('/v1/members/m1/photo').flush(null);
    http.expectOne('/v1/members/me').flush(profile());
    fixture.detectChanges();
    settlePhoto();

    expect(input.value).toBe('');
  });

  it('reports a refused upload without claiming anything was saved', () => {
    load(profile({ photoUrl: null }));

    choose(image());

    http.expectOne('/v1/members/m1/photo')
      .flush(
        { detail: 'That file is not a picture the platform accepts.' },
        { status: 400, statusText: 'Bad Request' },
      );
    fixture.detectChanges();

    // Deliberately not asserting on 'JPEG, PNG or WebP': the static help text
    // under the file input says exactly that, so this test would pass with no
    // error shown at all. It did, until the message was made distinctive.
    expect(text()).toContain('not a picture the platform accepts');
    expect(text()).not.toContain('Your photo has been updated');
  });

  it('offers no removal when there is no photo to remove', () => {
    load(profile({ photoUrl: null }));

    expect(text()).not.toContain('Remove photo');
  });

  it('offers removal once there is a photo', () => {
    load(profile({ photoUrl: '/v1/members/m1/photo' }));

    expect(text()).toContain('Remove photo');
  });

  it('removes the photo and re-reads', () => {
    load(profile({ photoUrl: '/v1/members/m1/photo' }));

    component.removePhoto();

    const removal = http.expectOne('/v1/members/m1/photo');
    expect(removal.request.method).toBe('DELETE');
    removal.flush(null);

    http.expectOne('/v1/members/me').flush(profile({ photoUrl: null }));
    fixture.detectChanges();

    expect(component.photoPath()).toBeNull();
    expect(text()).toContain('Your photo has been removed');
  });

  // ---- Change password ------------------------------------------------------

  function fillPasswordForm(overrides: Partial<typeof component.passwordForm> = {}): void {
    Object.assign(component.passwordForm, {
      current: 'old-password',
      next: 'a-new-long-password',
      confirm: 'a-new-long-password',
      ...overrides,
    });
  }

  it('changes the password and reports every other device was signed out', () => {
    load();
    fillPasswordForm();

    component.changePassword();

    const request = http.expectOne('/v1/identity/me/password');

    expect(request.request.body).toEqual({
      currentPassword: 'old-password',
      newPassword: 'a-new-long-password',
    });

    request.flush({ userId: 'm1', changedAt: '2026-01-01T10:00:00Z' });
    fixture.detectChanges();

    expect(text()).toContain('signed out');
  });

  it('clears the form once the password has actually changed', () => {
    load();
    fillPasswordForm();

    component.changePassword();
    http.expectOne('/v1/identity/me/password').flush({ userId: 'm1', changedAt: '2026-01-01T10:00:00Z' });

    expect(component.passwordForm.current).toBe('');
    expect(component.passwordForm.next).toBe('');
    expect(component.passwordForm.confirm).toBe('');
  });

  it('refuses a new password shorter than ten characters, without calling the server', () => {
    load();
    fillPasswordForm({ next: 'short', confirm: 'short' });

    component.changePassword();
    fixture.detectChanges();

    http.expectNone('/v1/identity/me/password');
    expect(component.showNewPasswordError()).toBe(true);
  });

  it('refuses a new password identical to the current one', () => {
    load();
    fillPasswordForm({ next: 'old-password', confirm: 'old-password' });

    component.changePassword();
    fixture.detectChanges();

    http.expectNone('/v1/identity/me/password');
    expect(component.showNewPasswordError()).toBe(true);
  });

  it('refuses a confirmation that does not match the new password', () => {
    load();
    fillPasswordForm({ confirm: 'something-else-long' });

    component.changePassword();
    fixture.detectChanges();

    http.expectNone('/v1/identity/me/password');
    expect(component.showConfirmError()).toBe(true);
  });

  it('shows the server error a wrong current password produces', () => {
    load();
    fillPasswordForm();

    component.changePassword();

    http.expectOne('/v1/identity/me/password').flush(
      { title: 'Auth.StepUpFailed', detail: 'Your current password is not correct.' },
      { status: 403, statusText: 'Forbidden' },
    );
    fixture.detectChanges();

    expect(component.passwordError()).not.toBeNull();
    expect(component.passwordForm.current).toBe('old-password');
  });
});
