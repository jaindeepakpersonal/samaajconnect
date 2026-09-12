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
    gautraId: null,
    professionId: null,
    qualificationId: null,
    permanentAddress: { line: null, cityId: null, localityId: 'loc-udaipur' },
    temporaryAddress: null,
    privacy: {
      mobile: 'SamaajOnly',
      email: 'Private',
      address: 'Private',
      profession: 'SamaajOnly',
      dateOfBirth: 'Private',
      qualification: 'SamaajOnly',
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

  /** Flushes the four master-data lists the screen fetches once on init. */
  function settleMasterData(): void {
    http.expectOne((r) => r.url === '/v1/master-data/gautras').flush([]);
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'ProfessionType')
      .flush([]);
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'Qualification')
      .flush([]);
    http.expectOne((r) => r.url === '/v1/master-data/cities').flush([]);
  }

  function load(me: MyProfile = profile()): void {
    fixture.detectChanges();
    settleMasterData();
    http.expectOne('/v1/members/me').flush(me);
    fixture.detectChanges();
    settlePhoto();
  }

  it('fills the form from what the server holds', () => {
    load();

    expect(component.form.fullName).toBe('Ravi Shah');
    expect(component.form.permanentAddress.localityId).toBe('loc-udaipur');
    expect(component.form.privacy.mobile).toBe('SamaajOnly');
    expect(component.form.isListedInDirectory).toBe(true);
  });

  it('seeds every optional field to an empty string, never undefined', () => {
    // An undefined bound to a control renders as an empty one that then posts
    // undefined — the same trap as a <select> with no matching option.
    load(profile({
      permanentAddress: { line: null, cityId: null, localityId: null },
      professionId: null,
      mobile: null,
    }));

    expect(component.form.permanentAddress.line).toBe('');
    expect(component.form.professionId).toBe('');
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

  it('sends null when a select is put back to Not set, not an empty string', () => {
    // "Cleared" and "blank" would otherwise be two different states in one
    // column, and only one of them reads as "not shared".
    load(profile({ professionId: 'prof-1' }));

    component.form.professionId = '';
    component.save();

    const request = http.expectOne('/v1/members/m1');

    expect(request.request.body.professionId).toBeNull();

    request.flush(profile({ professionId: null }));
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

  it('points to Privacy settings for who can see each field', () => {
    // The per-field privacy card moved to /privacy on 2026-09-11 - this
    // screen no longer says anything about hiding a member, it points there.
    load();

    expect(text()).toContain('Privacy settings');
  });

  it('shows the failure rather than a blank form', () => {
    fixture.detectChanges();
    settleMasterData();

    http.expectOne('/v1/members/me').flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
  });

  // ---- Tabs -----------------------------------------------------------------

  it('shows one card at a time, Basic first', () => {
    // The tab bar names all five sections regardless of which is active, so
    // this checks each card's own body content rather than its tab label.
    load();

    expect(text()).toContain('Basic information');
    expect(fixture.nativeElement.querySelector('#profession')).toBeNull();
    expect(fixture.nativeElement.querySelector('#qualification')).toBeNull();
    expect(fixture.nativeElement.querySelector('#permanent-line')).toBeNull();
    expect(fixture.nativeElement.querySelector('input#photo')).toBeNull();
  });

  it('switches cards on a tab click without losing what was typed elsewhere', () => {
    load();

    component.form.fullName = 'Ravi Shah Junior';

    const tabs = Array.from(
      fixture.nativeElement.querySelectorAll('[role="tab"]'),
    ) as HTMLButtonElement[];

    tabs.find((t) => t.textContent?.trim() === 'Professional')!.click();
    fixture.detectChanges();

    expect(text()).toContain('Professional');
    expect(text()).not.toContain('Basic information');
    // Switching tabs never touches `form` - a half-edited field on a tab that
    // scrolled out of view stays exactly as typed.
    expect(component.form.fullName).toBe('Ravi Shah Junior');
  });

  it('jumps back to Basic when a save is refused for having no name', () => {
    // The one client-side check this screen has only ever fails on a tab a
    // member may not be looking at - an error nobody can see is not a
    // validation message.
    load();

    component.activeTab.set('address');
    fixture.detectChanges();

    component.form.fullName = '   ';
    component.save();
    fixture.detectChanges();

    expect(component.activeTab()).toBe('basic');
    expect(text()).toContain('needs a name');
  });

  // ---- The photo ----------------------------------------------------------

  /**
   * The photo used to be a text field holding somebody else's URL. What
   * replaced it is a file input and its own request, because a file and a form
   * field are different things.
   */
  /**
   * Photo, like every other card, only renders while its own tab is active -
   * so the `scAuthedSrc` directive on an existing photo does not fire its own
   * request until this switches to it, and that request has to be settled
   * here rather than back in `load()`.
   */
  function openPhotoTab(): void {
    component.activeTab.set('photo');
    fixture.detectChanges();
    settlePhoto();
  }

  it('offers a file input rather than a link field', () => {
    load();
    openPhotoTab();

    const input: HTMLInputElement =
      fixture.nativeElement.querySelector('input#photo');

    expect(input.type).toBe('file');
    expect(input.accept).toBe('image/jpeg,image/png,image/webp');
  });

  it('says so plainly when the member has no photo', () => {
    load(profile({ photoUrl: null }));
    openPhotoTab();

    expect(text()).toContain('You have not added a photo');
  });

  function choose(file: File): void {
    openPhotoTab();

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

    choose(image());
    http.expectOne('/v1/members/m1/photo').flush(null);
    http.expectOne('/v1/members/me').flush(profile());
    fixture.detectChanges();
    settlePhoto();

    const input: HTMLInputElement =
      fixture.nativeElement.querySelector('input#photo');

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
    openPhotoTab();

    expect(text()).not.toContain('Remove photo');
  });

  it('offers removal once there is a photo', () => {
    load(profile({ photoUrl: '/v1/members/m1/photo' }));
    openPhotoTab();

    expect(text()).toContain('Remove photo');
  });

  it('removes the photo and re-reads', () => {
    load(profile({ photoUrl: '/v1/members/m1/photo' }));
    openPhotoTab();

    component.removePhoto();

    const removal = http.expectOne('/v1/members/m1/photo');
    expect(removal.request.method).toBe('DELETE');
    removal.flush(null);

    http.expectOne('/v1/members/me').flush(profile({ photoUrl: null }));
    fixture.detectChanges();

    expect(component.photoPath()).toBeNull();
    expect(text()).toContain('Your photo has been removed');
  });

  // ---- Gautra, Profession, Qualification and the address cascade -----------

  it('populates the Gautra, Profession and Qualification dropdowns from master-data-service', () => {
    fixture.detectChanges();

    http.expectOne((r) => r.url === '/v1/master-data/gautras')
      .flush([{ id: 'g1', name: 'Kasliwal', isActive: true }]);
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'ProfessionType')
      .flush([{ id: 'p1', category: 'ProfessionType', code: 'ARCH', label: 'Architect', sortOrder: 1, isActive: true }]);
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'Qualification')
      .flush([{ id: 'q1', category: 'Qualification', code: 'BCOM', label: 'B.Com', sortOrder: 1, isActive: true }]);
    http.expectOne((r) => r.url === '/v1/master-data/cities').flush([]);
    http.expectOne('/v1/members/me')
      .flush(profile({ gautraId: 'g1', professionId: 'p1', qualificationId: 'q1' }));
    fixture.detectChanges();
    settlePhoto();

    expect(component.gautras()).toEqual([{ id: 'g1', name: 'Kasliwal', isActive: true }]);
    // Gautra is on the Basic tab, active by default; Professional and
    // Educational are their own tabs, and have to be switched to before they
    // render anything.
    expect(text()).toContain('Kasliwal');

    component.activeTab.set('professional');
    fixture.detectChanges();

    expect(text()).toContain('Architect');

    component.activeTab.set('educational');
    fixture.detectChanges();

    expect(text()).toContain('B.Com');
    expect(component.form.gautraId).toBe('g1');
    expect(component.form.professionId).toBe('p1');
    expect(component.form.qualificationId).toBe('q1');
  });

  it('clears the Locality when the City changes, and loads that Citys Localities', () => {
    load();

    component.onCityChange('permanent', 'city-1');

    expect(component.form.permanentAddress.localityId).toBe('');

    const request = http.expectOne(
      (r) => r.url === '/v1/master-data/localities' && r.params.get('cityId') === 'city-1',
    );

    request.flush([{ id: 'loc-1', cityId: 'city-1', name: 'Hiran Magri', isActive: true }]);

    expect(component.permanentLocalities()).toEqual([
      { id: 'loc-1', cityId: 'city-1', name: 'Hiran Magri', isActive: true },
    ]);
  });

  it('same as permanent copies the address once rather than keeping it linked', () => {
    load();

    component.form.permanentAddress = { line: 'House 12', cityId: 'city-1', localityId: 'loc-1' };

    component.toggleSameAsPermanent(true);

    expect(component.form.temporaryAddress).toEqual(component.form.permanentAddress);
    expect(component.sameAsPermanent()).toBe(true);

    // A later edit to Permanent does not follow through to Temporary - this
    // was a one-time copy, not a link the two stay bound by.
    component.form.permanentAddress.line = 'A different house';

    expect(component.form.temporaryAddress.line).toBe('House 12');

    component.toggleSameAsPermanent(false);

    expect(component.sameAsPermanent()).toBe(false);
    expect(component.form.temporaryAddress.line).toBe('House 12');
  });

  it('sends null for the temporary address when nothing was filled in', () => {
    load();

    component.save();

    const request = http.expectOne('/v1/members/m1');

    expect(request.request.body.temporaryAddress).toBeNull();

    request.flush(profile());
  });

  it('sends the temporary address once any field on it is set', () => {
    load();

    component.form.temporaryAddress.line = 'A rented flat';
    component.save();

    const request = http.expectOne('/v1/members/m1');

    expect(request.request.body.temporaryAddress).toEqual({
      line: 'A rented flat',
      cityId: null,
      localityId: null,
    });

    request.flush(profile());
  });
});
