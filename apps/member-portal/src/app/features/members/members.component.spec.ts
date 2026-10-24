import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { API_CONFIG, CurrentUser } from '@samaajconnect/shared';
import { FamilyComponent } from './family.component';
import { MemberDetailComponent } from './member-detail.component';
import { MembersListComponent } from './members-list.component';
import { Child, Family, FamilyMember, Member, MemberHousehold } from './members.models';
import { MemberGroup } from '../groups/groups.models';

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
    gautraId: null,
    dateOfBirth: null,
    mobile: null,
    email: null,
    permanentAddress: { line: null, cityId: null, localityId: 'loc-hiran-magri' },
    temporaryAddress: null,
    professionId: 'prof-business',
    qualificationId: null,
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
    familyAdminMemberId: ME,
    familyHeadMemberId: null,
    familyCode: 'JAIN-4821',
    viewerIsAdmin: true,
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

  const surajpole = { id: 'loc-surajpole', cityId: 'city-1', name: 'Surajpole', isActive: true };
  const hiranMagri = { id: 'loc-hiran-magri', cityId: 'city-1', name: 'Hiran Magri', isActive: true };
  const kothari = { id: 'g-kothari', name: 'Kothari', isActive: true };

  /** Answers the Locality, Gautra and Profession lists, then the directory search. */
  function load(members: Member[]): void {
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/v1/master-data/localities').flush([surajpole, hiranMagri]);
    http.expectOne((r) => r.url === '/v1/master-data/gautras').flush([kothari]);
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'ProfessionType')
      .flush([{ id: 'prof-business', category: 'ProfessionType', code: 'BIZ', label: 'Business', sortOrder: 1, isActive: true }]);
    http.expectOne((r) => r.url === '/v1/members').flush({ items: members, hasMore: false });
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function cards(): HTMLElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.member-card'));
  }

  it('shows each member as a card: name with Gautra, father, mother, profession and locality', () => {
    load([
      member({ gautraId: kothari.id, fatherName: 'Mahesh Jain', motherName: 'Sunita Jain' }),
      member({ id: 'm2', fullName: 'Neha Jain', permanentAddress: { line: null, cityId: null, localityId: surajpole.id } }),
    ]);

    expect(cards().length).toBe(2);

    const first = cards()[0]!.textContent ?? '';
    expect(first).toContain('Rajesh Jain');
    expect(first).toContain('(Kothari)');
    expect(first).toContain('Mahesh Jain');
    expect(first).toContain('Sunita Jain');
    expect(first).toContain('Business');
    expect(first).toContain('Hiran Magri');

    expect(cards()[1]!.textContent).toContain('Surajpole');
  });

  it('leaves the father and mother lines off when the household records neither', () => {
    load([member()]);

    expect(cards()[0]!.textContent).not.toContain('Father');
    expect(cards()[0]!.textContent).not.toContain('Mother');
  });

  it('shows the photo through the authed image directive, and initials without one', () => {
    load([member({ photoUrl: '/v1/members/m1/photo' }), member({ id: 'm2', fullName: 'Neha Jain' })]);

    // The photo is fetched with the token; answer it so nothing is left open.
    http.expectOne('/v1/members/m1/photo').flush(new Blob(['x'], { type: 'image/png' }));

    expect(cards()[0]!.querySelector('img.member-photo')).not.toBeNull();
    expect(cards()[1]!.querySelector('.member-photo.initials')?.textContent?.trim()).toBe('NJ');
  });

  it('says a withheld field is not shared rather than claiming there is none', () => {
    // The service returns null rather than masking, so from here "not set" and
    // "not shared" are indistinguishable - and only one of them is safe to say.
    load([member({ professionId: null })]);

    expect(text()).toContain('Not shared');
  });

  it('filters by name, Gautra, Locality and Profession', () => {
    load([]);

    const labels = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('label')).map((l) =>
      l.textContent?.trim(),
    );
    expect(labels).toEqual(['Name', 'Gautra', 'Locality', 'Profession']);

    component.term = ' Neha ';
    component.gautraId = kothari.id;
    component.localityId = surajpole.id;
    component.professionId = 'prof-business';
    component.load();

    const request = http.expectOne((r) => r.url === '/v1/members');

    expect(request.request.params.get('term')).toBe('Neha');
    expect(request.request.params.get('gautraId')).toBe(kothari.id);
    expect(request.request.params.get('localityId')).toBe(surajpole.id);
    expect(request.request.params.get('professionId')).toBe('prof-business');

    request.flush({ items: [], hasMore: false });
  });

  it('offers every Locality, not only ones on the current page', () => {
    // The directory pages, so "values seen so far" would hide page five's.
    load([member()]);

    expect(component.localities()).toEqual([hiranMagri, surajpole]);
  });

  it('asks the service for the next page', () => {
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/v1/master-data/localities').flush([]);
    http.expectOne((r) => r.url === '/v1/master-data/gautras').flush([]);
    http.expectOne((r) => r.url === '/v1/master-data/lookup-items').flush([]);
    http.expectOne((r) => r.url === '/v1/members').flush({ items: [member()], hasMore: true });
    fixture.detectChanges();

    component.goTo(2);

    const request = http.expectOne((r) => r.url === '/v1/members');
    expect(request.request.params.get('skip')).toBe('20');
    request.flush({ items: [], hasMore: false });
  });

  it('tells an empty search apart from an empty directory, and clears back to everyone', () => {
    load([]);

    expect(text()).toContain('directory is empty');

    component.gautraId = kothari.id;
    component.load();
    http.expectOne((r) => r.url === '/v1/members').flush({ items: [], hasMore: false });
    fixture.detectChanges();

    expect(text()).toContain('Nobody matches that');

    component.clear();
    const cleared = http.expectOne((r) => r.url === '/v1/members');
    expect(cleared.request.params.has('gautraId')).toBe(false);
    cleared.flush({ items: [], hasMore: false });
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
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'm1' } }, paramMap: of(convertToParamMap({ id: 'm1' })) },
        },
      ],
    });

    fixture = TestBed.createComponent(MemberDetailComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** Flushes the four master-data lists the screen fetches once on init. */
  function settleMasterData(): void {
    http.expectOne((r) => r.url === '/v1/master-data/gautras').flush([]);
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'ProfessionType')
      .flush([{ id: 'prof-business', category: 'ProfessionType', code: 'BIZ', label: 'Business', sortOrder: 1, isActive: true }]);
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'Qualification')
      .flush([]);
    http.expectOne((r) => r.url === '/v1/master-data/localities').flush([]);
  }

  function load(
    found: Member,
    household: MemberHousehold = { household: null },
    groups: MemberGroup[] = [],
  ): void {
    fixture.detectChanges();
    settleMasterData();
    http.expectOne('/v1/members/m1').flush(found);
    fixture.detectChanges();
    http.expectOne('/v1/members/m1/household').flush(household);
    http.expectOne('/v1/volunteer-groups/members/m1/groups').flush(groups);
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('shows what the viewer may see', () => {
    load(member({ mobile: '+91 98765 43210', professionId: 'prof-business' }));

    expect(text()).toContain('+91 98765 43210');
    expect(text()).toContain('Business');
  });

  it('says "Not shared" for every field the service withheld', () => {
    load(member({
      mobile: null,
      email: null,
      permanentAddress: { line: null, cityId: null, localityId: null },
      dateOfBirth: null,
    }));

    const notShared = (text().match(/Not shared/g) ?? []).length;

    // Mobile, email, address, date of birth - and nothing claiming the member
    // simply has none of them.
    expect(notShared).toBeGreaterThanOrEqual(4);
  });

  it('opens as a profile page: photo or initials, name with Gautra', () => {
    load(member());

    const header = (fixture.nativeElement as HTMLElement).querySelector('app-profile-header')!;
    expect(header.textContent).toContain('Rajesh Jain');
    expect(header.querySelector('.profile-avatar.initials')?.textContent?.trim()).toBe('RJ');
  });

  it('shows the household and volunteer groups, each linking to its own page', () => {
    load(
      member({ fatherName: 'Mahesh Jain' }),
      {
        household: {
          familyId: 'f1',
          namedAfter: 'Mahesh Jain',
          members: [
            { memberId: 'h1', fullName: 'Mahesh Jain', photoUrl: null, relationshipToAdmin: null, isHead: true, isAdmin: true },
            { memberId: 'm1', fullName: 'Rajesh Jain', photoUrl: null, relationshipToAdmin: 'Child', isHead: false, isAdmin: false },
          ],
        },
      },
      [{ groupId: 'g1', name: 'Seva Group', focusArea: null, status: 'Active', photoUrl: null, roleTitle: 'Secretary', isPresident: false, joinedAt: '2026-01-01T00:00:00Z' }],
    );

    const page = fixture.nativeElement as HTMLElement;
    expect(text()).toContain("Mahesh Jain's household");
    expect(text()).toContain('Child of the family admin');
    expect(text()).toContain('Father');
    expect(page.querySelector('a[href="/members/h1"]')).not.toBeNull();
    expect(page.querySelector('a[href="/groups/g1"]')?.textContent).toContain('Seva Group');
    expect(text()).toContain('Secretary');
    expect(text()).not.toContain('is not shown yet');
  });

  it('says plainly when a member is in no household and no group', () => {
    load(member());

    expect(text()).toContain('Not in a household on the platform');
    expect(text()).toContain('Not in any volunteer group');
  });

  it('offers a retry when the profile cannot be loaded', () => {
    fixture.detectChanges();
    settleMasterData();
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

    http.expectOne('/v1/families/join-requests').flush(family({ viewerIsAdmin: false }));
    fixture.detectChanges();

    // The head has not decided yet.
    expect(component.family()).toBeNull();
    expect(text()).toContain('The family admin decides');
  });

  it('reports a real failure loading the household as an error', () => {
    fixture.detectChanges();
    http.expectOne('/v1/identity/me').flush(currentUser);
    http.expectOne('/v1/families/mine').flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();

    expect(component.error()).not.toBeNull();
  });

  // ---- The family head ----------------------------------------------------
  //
  // The family admin manages the household; the family head is its eldest
  // member, a label the admin chooses and that grants nothing.

  const headSelect = () => fixture.nativeElement.querySelector('#family-head') as HTMLSelectElement | null;

  it('lets the family admin choose a head from the active members', () => {
    load(
      family({
        members: [
          familyMember(),
          familyMember({ id: 'fm2', memberProfileId: 'p2', fullName: 'Anil Shah' }),
          familyMember({ id: 'fm3', memberProfileId: 'p3', fullName: 'Waiting Person', status: 'PendingJoinRequest' }),
        ],
      }),
    );

    const options = Array.from(headSelect()!.options).map((o) => o.textContent?.trim());

    expect(options).toContain('Anil Shah');
    expect(options).not.toContain('Waiting Person');
  });

  it('offers no head control to somebody who is not the admin', () => {
    load(family({ viewerIsAdmin: false, familyCode: null, familyAdminMemberId: HEAD }));

    expect(headSelect()).toBeNull();
  });

  it('saves the chosen head without touching who administers the household', () => {
    load(
      family({
        members: [familyMember(), familyMember({ id: 'fm2', memberProfileId: 'p2', fullName: 'Anil Shah' })],
      }),
    );

    component.headChoice = 'p2';
    component.saveHead(component.family()!);

    const call = http.expectOne('/v1/families/mine/head');
    expect(call.request.method).toBe('PUT');
    expect(call.request.body).toEqual({ memberProfileId: 'p2' });

    call.flush(family({ familyHeadMemberId: 'p2' }));
    fixture.detectChanges();

    expect(component.family()?.familyHeadMemberId).toBe('p2');
    expect(component.family()?.familyAdminMemberId).toBe(ME);
  });

  it('clears the head when Not chosen is picked', () => {
    load(family({ familyHeadMemberId: 'p2' }));

    component.headChoice = '';
    component.saveHead(component.family()!);

    expect(http.expectOne('/v1/families/mine/head').request.body).toEqual({ memberProfileId: null });
  });

  it('shows why a refused head choice was refused', () => {
    load(family());

    component.headChoice = 'stranger';
    component.saveHead(component.family()!);
    http.expectOne('/v1/families/mine/head').flush(
      { title: 'Family.HeadNotAMember', detail: 'The family head has to be an active member of this household.' },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();

    expect(component.headError()).not.toBeNull();
  });

  it('marks the family head in the list of people', () => {
    load(
      family({
        familyHeadMemberId: 'p2',
        members: [familyMember(), familyMember({ id: 'fm2', memberProfileId: 'p2', fullName: 'Anil Shah' })],
      }),
    );

    expect(text()).toContain('Anil Shah (family head)');
  });

  // ---- The family code ----------------------------------------------------

  it('shows the family code to the family admin', () => {
    load(family({ viewerIsAdmin: true, familyCode: 'JAIN-4821' }));

    expect(text()).toContain('JAIN-4821');
  });

  it('shows no code to an ordinary member, because the service sends none', () => {
    // It is the token anyone needs to request to join; handing it to every
    // member would let any one of them invite the Samaaj into the household.
    load(family({ viewerIsAdmin: false, familyCode: null, familyAdminMemberId: HEAD }));

    expect(text()).not.toContain('JAIN-4821');
    expect(text()).toContain('Only the family admin');
  });

  it('offers the family admin the requests waiting on them', () => {
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

    component.decide(component.family()!, component.pendingRequests(component.family()!)[0]!, true);

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

    component.beginConversion(component.children()[0]!);
    component.contact = '  aarav@example.com  ';
    component.startConversion(component.children()[0]!);

    const request = http.expectOne('/v1/children/c1/conversion');

    expect(request.request.body).toEqual({ mobileOrEmail: 'aarav@example.com' });

    request.flush({});
    http.expectOne('/v1/children').flush([child({ hasPendingConversion: true })]);
    fixture.detectChanges();

    expect(component.children()[0]!.hasPendingConversion).toBe(true);
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

  // ---- Withdrawing parental consent (DPDP s.6(4)) -------------------------
  //
  // The right existed and was unreachable from this screen: the only way to
  // withdraw was to erase your whole account. Giving was one tick, here.

  function withConsent(givenBy: string) {
    return child({
      parentalConsent: {
        givenByMemberId: givenBy,
        noticeVersion: 'child-notice-v1',
        attestation: 'I confirm.',
        givenAt: new Date().toISOString(),
      },
    });
  }

  it('offers withdrawing to the person whose consent it is', () => {
    load(family(), [withConsent(ME)]);

    expect(buttonSaying('Withdraw parental consent')).toBeDefined();
  });

  it('and to nobody else, because the consent is not theirs to take back', () => {
    // A head who inherited the household did not give this consent. Section
    // 6(4) is a right over your own consent, not over the household's records.
    load(family(), [withConsent('somebody-else')]);

    expect(buttonSaying('Withdraw parental consent')).toBeUndefined();
  });

  it('says what withdrawing destroys before doing it', () => {
    load(family(), [withConsent(ME)]);

    buttonSaying('Withdraw parental consent')!.click();
    fixture.detectChanges();

    // Not "are you sure?". Section 6(4) is about ease, and this screen's own
    // rule is that a reversible withdrawal takes no confirmation - but this one
    // cannot be undone, and an unguarded click on that is a trap rather than
    // ease.
    expect(text()).toContain('nothing on this platform can bring them back');
  });

  it('leaves the trigger in place while the panel is open', () => {
    load(family(), [withConsent(ME)]);

    buttonSaying('Withdraw parental consent')!.click();
    fixture.detectChanges();

    const trigger = buttonSaying('Withdraw parental consent')!;

    expect(trigger).toBeDefined();
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    expect(trigger.disabled).toBe(false);
  });

  it('withdraws and re-reads the household', () => {
    load(family(), [withConsent(ME)]);

    component.withdrawConsent(component.children()[0]!);

    http.expectOne('/v1/children/c1/parental-consent').flush({ withdrawn: true });

    // A full re-read of the household: the child leaves the list, and what the
    // member may do next changes with it - the last member of a household with
    // children cannot leave, and now they can. `/identity/me` is not refetched;
    // it is read once on init.
    http.expectOne('/v1/families/mine').flush(family());
    http.expectOne('/v1/children').flush([]);

    fixture.detectChanges();

    expect(component.children()).toHaveLength(0);
  });

  it('shows a refusal against that child rather than blanking the screen', () => {
    load(family(), [withConsent(ME)]);

    component.withdrawConsent(component.children()[0]!);

    http.expectOne('/v1/children/c1/parental-consent').flush(
      { title: 'Conflict', detail: 'This person has their own account now.' },
      { status: 409, statusText: 'Conflict' },
    );

    fixture.detectChanges();

    expect(component.error()).toBeNull();
    expect(component.consentError()['c1']).toContain('their own account now');
    expect(text()).toContain('their own account now');
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

  // ---- Waiting on a head who has not decided ------------------------------
  //
  // `/families/mine` answers with the household for somebody whose request is
  // only pending, so this screen used to draw it as theirs - and nothing could
  // take the request back, while a pending request blocks joining anywhere else
  // or creating one of your own.

  function waitingOn(headId: string) {
    return family({
      familyAdminMemberId: headId,
      viewerIsAdmin: false,
      members: [
        familyMember({ id: 'fm-head', memberProfileId: headId, fullName: 'Anil Shah' }),
        familyMember({
          id: 'fm-me',
          memberProfileId: ME,
          fullName: 'Ravi Shah',
          status: 'PendingJoinRequest',
          decidedAt: null,
        }),
      ],
    });
  }

  it('does not draw a household as yours while the request is only pending', () => {
    load(waitingOn(HEAD));

    expect(text()).toContain('Waiting to join a household');
    expect(text()).not.toContain('Your household');
  });

  it('names who is being asked rather than printing an id', () => {
    load(waitingOn(HEAD));

    expect(text()).toContain('Anil Shah');
    expect(text()).not.toContain(HEAD);
  });

  it('says plainly what the request costs while it stands', () => {
    load(waitingOn(HEAD));

    expect(text()).toContain('cannot join another household or create your own');
  });

  it('withdraws the request and re-reads', () => {
    load(waitingOn(HEAD));

    component.withdrawRequest();

    const withdrawal = http.expectOne('/v1/families/join-requests/mine');
    expect(withdrawal.request.method).toBe('DELETE');
    withdrawal.flush({ withdrawn: true });

    // Only the household:  does not refetch /identity/me, which is
    // read once on init.
    http.expectOne('/v1/families/mine').flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('Create a household');
  });

  /**
   * The head accepted while this screen was open. The refusal is shown and the
   * screen re-reads, so the member sees they are in the household rather than
   * being told nothing happened.
   */
  it('shows a refusal rather than swallowing it', () => {
    load(waitingOn(HEAD));

    component.withdrawRequest();

    http.expectOne('/v1/families/join-requests/mine').flush(
      { detail: 'Your request was accepted, so you are in that household now.' },
      { status: 409, statusText: 'Conflict' },
    );

    http.expectOne('/v1/families/mine').flush(
      family({ members: [familyMember({ memberProfileId: ME, status: 'Active' })] }),
    );
    http.expectOne('/v1/children').flush([]);
    fixture.detectChanges();
    settlePhotos();

    expect(text()).toContain('accepted');
  });

  it('draws the ordinary household screen once the request is accepted', () => {
    load(family({ viewerIsAdmin: false }));

    expect(text()).toContain('Your household');
    expect(text()).not.toContain('Waiting to join a household');
  });

  // ---- Leaving a household ------------------------------------------------

  it('offers leaving to an ordinary member, not only the head', () => {
    load(family({ viewerIsAdmin: false }));

    expect(text()).toContain('Leave this household');
  });

  /**
   * The trigger stays and stays enabled. A confirmation that replaces its own
   * trigger drops keyboard focus to the body - the finding three admin screens
   * shared in the 2026-09-02 accessibility pass.
   */
  it('keeps the trigger and says what it did', () => {
    load(family());

    const button = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('button'),
    ).find((b) => b.textContent?.trim() === 'Leave this household')!;

    expect(button.getAttribute('aria-expanded')).toBe('false');

    component.askToLeave();
    fixture.detectChanges();

    expect(button.isConnected).toBe(true);
    expect(button.disabled).toBe(false);
    expect(button.getAttribute('aria-expanded')).toBe('true');
  });

  it('warns the head that the household passes on', () => {
    load(family({ viewerIsAdmin: true }));

    component.askToLeave();
    fixture.detectChanges();

    expect(text()).toContain('longest-standing member takes over');
  });

  it('does not warn an ordinary member about succession', () => {
    load(family({ viewerIsAdmin: false }));

    component.askToLeave();
    fixture.detectChanges();

    expect(text()).not.toContain('longest-standing member takes over');
  });

  it('leaves and re-reads', () => {
    load(family());

    component.leaveFamily();

    const left = http.expectOne('/v1/families/mine/membership');
    expect(left.request.method).toBe('DELETE');
    left.flush({ left: true, newHeadMemberId: null });

    http.expectOne('/v1/families/mine').flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('Create a household');
  });

  /**
   * The refusal that matters: the last member of a household with children is
   * told why rather than silently failing.
   */
  it('shows the refusal when leaving would strand children', () => {
    load(family());

    component.leaveFamily();

    http.expectOne('/v1/families/mine/membership').flush(
      {
        detail:
          'You are the only person left in this household and it has children'
          + " records in it. Leaving would leave nobody able to manage them.",
      },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();

    expect(text()).toContain('nobody able to manage them');
  });
});
