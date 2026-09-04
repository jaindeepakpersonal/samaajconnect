import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG, CurrentUser } from '@samaajconnect/shared';
import { FamilyComponent } from './family.component';
import { MemberDetailComponent } from './member-detail.component';
import { MembersListComponent } from './members-list.component';
import { Child, Family, FamilyMember, Member } from './members.models';

const ME = 'u1';
const HEAD = 'h1';

const currentUser: CurrentUser = {
  userId: ME,
  tenantId: 't1',
  tenantSlug: 'mahavir-samaj',
  mobileOrEmail: 'ravi@example.com',
  fullName: 'Ravi Shah',
  status: 'Active',
  isContactVerified: true,
  lastLoginAt: null,
  roles: ['Member'],
  permissions: ['Members.Read'],
};

function member(overrides: Partial<Member> = {}): Member {
  return {
    id: 'm1',
    fullName: 'Rajesh Jain',
    photoUrl: null,
    locality: 'Hiran Magri',
    dateOfBirth: null,
    mobile: null,
    email: null,
    address: null,
    profession: 'Business',
    gender: 'Male',
    ...overrides,
  };
}

function familyMember(overrides: Partial<FamilyMember> = {}): FamilyMember {
  return {
    id: 'fm1',
    memberProfileId: ME,
    fullName: 'Ravi Shah',
    relationship: 'Spouse',
    status: 'Active',
    requestedAt: new Date().toISOString(),
    decidedAt: new Date().toISOString(),
    ...overrides,
  };
}

function family(overrides: Partial<Family> = {}): Family {
  return {
    id: 'f1',
    familyHeadMemberId: ME,
    familyCode: 'JAIN-4821',
    viewerIsHead: true,
    members: [familyMember()],
    createdAt: new Date().toISOString(),
    ...overrides,
  };
}

function child(overrides: Partial<Child> = {}): Child {
  return {
    id: 'c1',
    familyId: 'f1',
    fullName: 'Anaya Jain',
    dateOfBirth: '2015-02-02',
    age: 11,
    gender: 'Female',
    photoUrl: null,
    status: 'Minor',
    isEligibleForConversion: false,
    hasPendingConversion: false,
    createdAt: new Date().toISOString(),
    parentalConsent: null,
    ...overrides,
  };
}

function providers() {
  return [
    provideRouter([]),
    provideHttpClient(),
    provideHttpClientTesting(),
    { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
  ];
}

describe('MembersListComponent', () => {
  let fixture: ComponentFixture<MembersListComponent>;
  let component: MembersListComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [MembersListComponent], providers: providers() });

    fixture = TestBed.createComponent(MembersListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function load(members: Member[]): void {
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/v1/members').flush(members);
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('lists the directory', () => {
    load([member(), member({ id: 'm2', fullName: 'Neha Jain', locality: 'Surajpole' })]);

    expect(text()).toContain('Rajesh Jain');
    expect(text()).toContain('Neha Jain');
  });

  it('says a withheld field is not shared rather than claiming there is none', () => {
    // The service returns null rather than masking, so from here "not set" and
    // "not shared" are indistinguishable - and only one of them is safe to say.
    load([member({ profession: null })]);

    expect(text()).toContain('Not shared');
  });

  it('offers no profession filter, because profession can be private', () => {
    // A server-side filter on it would let anybody confirm a private value one
    // query at a time - the same reason the service refuses to match a search
    // term against a private mobile number.
    load([member()]);

    const labels = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('label'),
    ).map((l) => l.textContent?.trim());

    expect(labels).toContain('Search by name');
    expect(labels).toContain('Locality');
    expect(labels).not.toContain('Profession');
  });

  it('builds the locality list from the data, not the wireframe three', () => {
    load([
      member({ locality: 'Surajpole' }),
      member({ id: 'm2', locality: 'Hiran Magri' }),
      member({ id: 'm3', locality: null }),
    ]);

    expect(component.localities()).toEqual(['Hiran Magri', 'Surajpole']);
  });

  it('keeps localities a narrower search filtered out', () => {
    // Otherwise choosing a locality would empty the very list that offered it.
    load([member({ locality: 'Surajpole' }), member({ id: 'm2', locality: 'Hiran Magri' })]);

    component.locality = 'Surajpole';
    component.load();
    http.expectOne((r) => r.url === '/v1/members').flush([member({ locality: 'Surajpole' })]);
    fixture.detectChanges();

    expect(component.localities()).toEqual(['Hiran Magri', 'Surajpole']);
  });

  it('sends the search term and locality to the service', () => {
    load([]);

    component.term = 'Neha';
    component.locality = 'Surajpole';
    component.load();

    const request = http.expectOne((r) => r.url === '/v1/members');

    expect(request.request.params.get('term')).toBe('Neha');
    expect(request.request.params.get('locality')).toBe('Surajpole');

    request.flush([]);
  });

  it('tells an empty search apart from an empty directory', () => {
    load([]);

    expect(text()).toContain('directory is empty');

    component.term = 'Nobody';
    component.load();
    http.expectOne((r) => r.url === '/v1/members').flush([]);
    fixture.detectChanges();

    expect(text()).toContain('Nobody matches that');
  });
});

describe('MemberDetailComponent', () => {
  let fixture: ComponentFixture<MemberDetailComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MemberDetailComponent],
      providers: [
        ...providers(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'm1' } } } },
      ],
    });

    fixture = TestBed.createComponent(MemberDetailComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function load(found: Member): void {
    fixture.detectChanges();
    http.expectOne('/v1/members/m1').flush(found);
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('shows what the viewer may see', () => {
    load(member({ mobile: '+91 98765 43210', profession: 'Business' }));

    expect(text()).toContain('+91 98765 43210');
    expect(text()).toContain('Business');
  });

  it('says "Not shared" for every field the service withheld', () => {
    load(member({ mobile: null, email: null, address: null, dateOfBirth: null }));

    const notShared = (text().match(/Not shared/g) ?? []).length;

    // Mobile, email, address, date of birth - and nothing claiming the member
    // simply has none of them.
    expect(notShared).toBeGreaterThanOrEqual(4);
  });

  it('does not invent the family and volunteer group the wireframe showed', () => {
    // Neither service exposes those per member.
    load(member());

    expect(text()).toContain('will not guess');
    expect(text()).not.toContain('Seva Group');
  });

  it('offers a retry when the profile cannot be loaded', () => {
    fixture.detectChanges();
    http.expectOne('/v1/members/m1').flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('Try again');
  });
});

describe('FamilyComponent', () => {
  let fixture: ComponentFixture<FamilyComponent>;
  let component: FamilyComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [FamilyComponent], providers: providers() });

    // jsdom implements neither, and the directive that renders a child's photo
    // uses both.
    URL.createObjectURL = () => 'blob:test';
    URL.revokeObjectURL = () => undefined;

    fixture = TestBed.createComponent(FamilyComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** Answers /me, the household and its children. */
  function load(household: Family | null, children: Child[] = []): void {
    fixture.detectChanges();

    http.expectOne('/v1/identity/me').flush(currentUser);

    const request = http.expectOne('/v1/families/mine');

    if (household === null) {
      request.flush({}, { status: 404, statusText: 'Not Found' });
    } else {
      request.flush(household);
      http.expectOne('/v1/children').flush(children);
    }

    fixture.detectChanges();
    settlePhotos();
  }

  /**
   * Flushes the photo requests the `scAuthedSrc` directive makes.
   *
   * A child with a photo now costs a second request: the image goes through
   * `HttpClient` so the auth interceptor can attach the token, because a plain
   * `<img src>` is fetched by the browser with no Authorization header at all.
   * A test that leaves one open fails `http.verify()` in `afterEach` and leaves
   * the TestBed dirty for every test after it.
   */
  function settlePhotos(): void {
    for (const request of http.match((r) => r.url.endsWith('/photo') && r.method === 'GET')) {
      request.flush(new Blob(['x']));
    }

    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function buttonSaying(label: string): HTMLButtonElement | undefined {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((b) => b.textContent?.trim().startsWith(label)) as HTMLButtonElement | undefined;
  }

  // ---- No household yet ---------------------------------------------------

  it('treats having no household as a state, not an error', () => {
    // 404 here is the ordinary case for somebody who has not joined one.
    load(null);

    expect(component.error()).toBeNull();
    expect(buttonSaying('Create a household')).toBeDefined();
    expect(buttonSaying('Request to join')).toBeDefined();
  });

  it('does not show a pending join request as though it were their household', () => {
    load(null);

    component.familyCode = 'JAIN-4821';
    component.join();

    http.expectOne('/v1/families/join-requests').flush(family({ viewerIsHead: false }));
    fixture.detectChanges();

    // The head has not decided yet.
    expect(component.family()).toBeNull();
    expect(text()).toContain('The family head decides');
  });

  it('reports a real failure loading the household as an error', () => {
    fixture.detectChanges();
    http.expectOne('/v1/identity/me').flush(currentUser);
    http.expectOne('/v1/families/mine').flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();

    expect(component.error()).not.toBeNull();
  });

  // ---- The family code ----------------------------------------------------

  it('shows the family code to the head', () => {
    load(family({ viewerIsHead: true, familyCode: 'JAIN-4821' }));

    expect(text()).toContain('JAIN-4821');
  });

  it('shows no code to an ordinary member, because the service sends none', () => {
    // It is the token anyone needs to request to join; handing it to every
    // member would let any one of them invite the Samaaj into the household.
    load(family({ viewerIsHead: false, familyCode: null, familyHeadMemberId: HEAD }));

    expect(text()).not.toContain('JAIN-4821');
    expect(text()).toContain('Only the family head');
  });

  it('offers the head the requests waiting on them', () => {
    load(
      family({
        members: [
          familyMember(),
          familyMember({ id: 'fm2', memberProfileId: 'x', fullName: 'Neha Jain',
            status: 'PendingJoinRequest' }),
        ],
      }),
    );

    expect(component.pendingRequests(component.family()!)).toHaveLength(1);
    expect(component.activeMembers(component.family()!)).toHaveLength(1);
    expect(buttonSaying('Accept')).toBeDefined();
  });

  it('keeps a refused decision beside its own request', () => {
    load(
      family({
        members: [familyMember({ id: 'fm2', memberProfileId: 'x', status: 'PendingJoinRequest' })],
      }),
    );

    component.decide(component.family()!, component.pendingRequests(component.family()!)[0], true);

    http
      .expectOne('/v1/families/f1/join-requests/fm2/decide')
      .flush({}, { status: 500, statusText: 'Server Error' });

    fixture.detectChanges();

    expect(component.error()).toBeNull();
    expect(component.actionError()['fm2']).toBeDefined();
  });

  // ---- Adding a child: the DPDP obligation --------------------------------

  it('fetches the data notice before offering the form', () => {
    // DPDP s.9 makes parental consent the basis for holding a child's data,
    // and a tick beside a notice that has not arrived is a tick against
    // nothing.
    load(family());

    component.beginAddChild();

    http.expectOne('/v1/children/data-notice').flush({
      version: 'child-notice-v1',
      summary: 'What we keep about a child, and why.',
      attestation: 'I confirm I am this child’s parent or guardian.',
    });

    fixture.detectChanges();

    expect(text()).toContain('What we keep about a child');
    expect(text()).toContain('parent or guardian');
  });

  it('never pre-fills the consent tick', () => {
    load(family());

    component.beginAddChild();
    http.expectOne('/v1/children/data-notice').flush({
      version: 'child-notice-v1', summary: 'Summary.', attestation: 'I confirm.',
    });
    fixture.detectChanges();

    expect(component.consentGiven).toBe(false);
  });

  it('will not add a child without the consent tick', () => {
    load(family());

    component.beginAddChild();
    http.expectOne('/v1/children/data-notice').flush({
      version: 'child-notice-v1', summary: 'Summary.', attestation: 'I confirm.',
    });

    component.childName = 'Anaya Jain';
    component.childDob = '2015-02-02';
    component.consentGiven = false;
    component.addChild(component.notice()!);

    // Nothing sent - http.verify() in afterEach is the assertion.
    expect(component.children()).toHaveLength(0);
  });

  it('sends the notice version with the consent, so the record says what was shown', () => {
    load(family());

    component.beginAddChild();
    http.expectOne('/v1/children/data-notice').flush({
      version: 'child-notice-v3', summary: 'Summary.', attestation: 'I confirm.',
    });

    component.childName = '  Anaya Jain  ';
    component.childDob = '2015-02-02';
    component.childGender = 'Female';
    component.consentGiven = true;
    component.addChild(component.notice()!);

    const request = http.expectOne('/v1/children');

    expect(request.request.body).toEqual({
      fullName: 'Anaya Jain',
      dateOfBirth: '2015-02-02',
      gender: 'Female',
      parentalConsentGiven: true,
      noticeVersion: 'child-notice-v3',
    });

    request.flush(child());
    fixture.detectChanges();

    expect(component.children()).toHaveLength(1);
  });

  // ---- Conversion ---------------------------------------------------------

  it('offers conversion only to a child who is eligible', () => {
    load(family(), [child({ isEligibleForConversion: false })]);

    expect(buttonSaying('Register as a main account')).toBeUndefined();
  });

  it('offers it to one who is, and says what is preserved', () => {
    load(family(), [child({ age: 18, isEligibleForConversion: true })]);

    expect(buttonSaying('Register as a main account')).toBeDefined();
    expect(text()).toContain('Pathshala history are kept');
  });

  it('says a request is with a Samaaj admin rather than offering it again', () => {
    load(family(), [child({ isEligibleForConversion: true, hasPendingConversion: true })]);

    expect(buttonSaying('Register as a main account')).toBeUndefined();
    expect(text()).toContain('Waiting for a Samaaj admin');
  });

  it('sends the contact the parent typed and re-reads the children', () => {
    load(family(), [child({ isEligibleForConversion: true })]);

    component.beginConversion(component.children()[0]);
    component.contact = '  aarav@example.com  ';
    component.startConversion(component.children()[0]);

    const request = http.expectOne('/v1/children/c1/conversion');

    expect(request.request.body).toEqual({ mobileOrEmail: 'aarav@example.com' });

    request.flush({});
    http.expectOne('/v1/children').flush([child({ hasPendingConversion: true })]);
    fixture.detectChanges();

    expect(component.children()[0].hasPendingConversion).toBe(true);
  });

  it('shows a converted child as having their own account', () => {
    load(family(), [child({ status: 'Converted' })]);

    expect(text()).toContain('Has their own account');
  });

  it('shows the consent a child record rests on', () => {
    load(family(), [
      child({
        parentalConsent: {
          givenByMemberId: ME,
          noticeVersion: 'child-notice-v1',
          attestation: 'I confirm.',
          givenAt: new Date().toISOString(),
        },
      }),
    ]);

    expect(text()).toContain('child-notice-v1');
  });

  it('does not offer an invitation it cannot send', () => {
    // The wireframe has an Invite button; there is no notification channel.
    load(family());

    expect(buttonSaying('Invite')).toBeUndefined();
    expect(text()).toContain('no notification channel');
  });

  it('still shows the household when the children call fails', () => {
    fixture.detectChanges();
    http.expectOne('/v1/identity/me').flush(currentUser);
    http.expectOne('/v1/families/mine').flush(family());
    http.expectOne('/v1/children').flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(component.error()).toBeNull();
    expect(text()).toContain('Your household');
  });

  // ---- A child's photo ----------------------------------------------------
  //
  // The reason the platform hosts images at all. A child's photo used to be a
  // URL, so every viewer of the record told a third-party host that a child's
  // picture had just been looked at - the tracking DPDP s.9(3) prohibits.

  function chooseFor(childId: string, file: File): void {
    const input: HTMLInputElement =
      fixture.nativeElement.querySelector(`input#child-photo-${childId}`);

    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function picture(bytes = 10): File {
    return new File([new Uint8Array(bytes)], 'photo.png', { type: 'image/png' });
  }

  it('offers a file input for each child rather than a link field', () => {
    load(family(), [child()]);

    const input: HTMLInputElement =
      fixture.nativeElement.querySelector('input#child-photo-c1');

    expect(input.type).toBe('file');
    expect(input.accept).toBe('image/jpeg,image/png,image/webp');
    expect(text()).toContain('Add a photo');
  });

  it('uploads a chosen photo as multipart and re-reads the children', () => {
    load(family(), [child()]);

    chooseFor('c1', picture());

    const upload = http.expectOne('/v1/children/c1/photo');
    expect(upload.request.method).toBe('POST');
    expect(upload.request.body).toBeInstanceOf(FormData);
    // The browser writes the multipart boundary; a Content-Type set by hand
    // would have none and no server could parse the body.
    expect(upload.request.headers.has('Content-Type')).toBe(false);

    upload.flush(null);

    http.expectOne('/v1/children').flush([child({ photoUrl: '/v1/children/c1/photo' })]);
    fixture.detectChanges();
    settlePhotos();

    expect(text()).toContain('Replace photo');
  });

  it('refuses a photo over 2 MB without sending it', () => {
    load(family(), [child()]);

    chooseFor('c1', picture(2 * 1024 * 1024 + 1));

    http.verify();
    expect(text()).toContain('larger than 2 MB');
  });

  it('reports a refused upload against the child it was for', () => {
    load(family(), [child(), child({ id: 'c2', fullName: 'Vivaan Jain' })]);

    chooseFor('c2', picture());

    http.expectOne('/v1/children/c2/photo').flush(
      { detail: 'That file is not a picture the platform accepts.' },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(component.photoError()['c2']).toContain('not a picture the platform accepts');
    expect(component.photoError()['c1']).toBeUndefined();
  });

  it('offers removal only once a child has a photo', () => {
    load(family(), [child({ photoUrl: '/v1/children/c1/photo' })]);

    expect(text()).toContain('Remove photo');

    component.removeChildPhoto(child({ photoUrl: '/v1/children/c1/photo' }));

    const removal = http.expectOne('/v1/children/c1/photo');
    expect(removal.request.method).toBe('DELETE');
    removal.flush(null);

    http.expectOne('/v1/children').flush([child({ photoUrl: null })]);
    fixture.detectChanges();
    settlePhotos();

    expect(text()).not.toContain('Remove photo');
  });
});
