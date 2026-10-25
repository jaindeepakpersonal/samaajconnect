import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG, CurrentUser } from '@samaajconnect/shared';
import { GroupDetailComponent } from './group-detail.component';
import { GroupsListComponent } from './groups-list.component';
import {
  Election,
  ElectionCandidate,
  ElectionDetail,
  GroupApplication,
  GroupDetail,
  GroupMember,
  GroupRole,
  VolunteerGroup,
} from './groups.models';
import { Member } from '../members/members.models';

const ME = 'u1';
const PRESIDENT = 'p1';

const ROLES: GroupRole[] = [
  { id: 'rp', title: 'President', kind: 'President', seats: 1, holderCount: 1, isEditable: false, isElected: true },
  { id: 'rs', title: 'Secretary', kind: 'Elected', seats: 1, holderCount: 0, isEditable: true, isElected: true },
  { id: 'rc', title: 'Coordinator', kind: 'Elected', seats: 2, holderCount: 0, isEditable: true, isElected: true },
  { id: 'rv', title: 'Volunteer', kind: 'Volunteer', seats: null, holderCount: 1, isEditable: false, isElected: false },
];

const member: CurrentUser = {
  userId: ME,
  tenantId: 't1',
  tenantSlug: 'mahavir-samaj',
  mobileOrEmail: 'ravi@example.com',
  fullName: 'Ravi Shah',
  status: 'Active',
  isContactVerified: true,
  lastLoginAt: null,
  roles: ['Member'],
  permissions: ['Members.Read', 'VolunteerGroups.Lead'],
};

function group(overrides: Partial<VolunteerGroup> = {}): VolunteerGroup {
  return {
    id: 'g1',
    name: 'Seva Group',
    description: 'Food drives, blood donation camps and elderly support.',
    focusArea: 'Social Service',
    presidentMemberId: PRESIDENT,
    status: 'Active',
    memberCount: 82,
    pendingApplicationCount: 0,
    myApplicationStatus: null,
    iAmAMember: false,
    iAmThePresident: false,
    createdAt: new Date().toISOString(),
    ...overrides,
    // Whoever is president can lead; a test naming neither says nothing else.
    iCanLead: overrides.iCanLead ?? overrides.iAmThePresident ?? false,
    iCanRunElections: overrides.iCanRunElections ?? overrides.iAmThePresident ?? false,
  };
}

function detail(
  overrides: Partial<VolunteerGroup> = {},
  members: GroupMember[] = [],
): GroupDetail {
  return { group: group(overrides), members, roles: ROLES };
}

/** A minimal member-family-service response, for the name-resolution calls `group-detail.component.ts` now makes. */
function apiMember(id: string, fullName = 'Some Member'): Member {
  return {
    id,
    fullName,
    photoUrl: null,
    gautraId: null,
    dateOfBirth: null,
    mobile: null,
    email: null,
    permanentAddress: { line: null, cityId: null, localityId: null },
    temporaryAddress: null,
    professionId: null,
    qualificationId: null,
    gender: 'Unspecified',
  };
}

function application(overrides: Partial<GroupApplication> = {}): GroupApplication {
  return {
    id: 'a1',
    memberId: 'm9',
    note: 'I can help on Sundays.',
    status: 'Pending',
    decidedBy: null,
    decidedAt: null,
    createdAt: new Date().toISOString(),
    ...overrides,
  };
}

const SOON = new Date(Date.now() + 7 * 86400000).toISOString();

function election(overrides: Partial<Election> = {}): Election {
  return {
    id: 'e1',
    groupId: 'g1',
    positionTitle: 'Secretary',
    nominationStartAt: SOON,
    nominationEndAt: SOON,
    votingStartAt: SOON,
    votingEndAt: SOON,
    status: 'NominationsOpen',
    acceptsNominations: true,
    acceptsVotes: false,
    myVoteCandidateIds: [],
    candidateCount: 0,
    winnerMemberIds: [],
    publishedAt: null,
    roleId: 'rs',
    seats: 1,
    createdAt: new Date().toISOString(),
    ...overrides,
  };
}

function candidate(overrides: Partial<ElectionCandidate> = {}): ElectionCandidate {
  return {
    id: 'c1',
    memberId: 'm9',
    status: 'Nominated',
    nominatedBy: PRESIDENT,
    votes: 0,
    ...overrides,
  };
}

function electionDetail(
  overrides: Partial<Election> = {},
  candidates: ElectionCandidate[] = [],
): ElectionDetail {
  return { election: election(overrides), candidates };
}

function providers() {
  return [
    provideRouter([]),
    provideHttpClient(),
    provideHttpClientTesting(),
    { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
  ];
}

// jsdom implements neither, and the directive that renders a hosted photo uses
// both. Set for the whole file: a photo drawn by a test that did not stub them
// is an unhandled error after it, which fails the run in CI.
URL.createObjectURL = () => 'blob:test';
URL.revokeObjectURL = () => undefined;

describe('GroupsListComponent', () => {
  let fixture: ComponentFixture<GroupsListComponent>;
  let component: GroupsListComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [GroupsListComponent], providers: providers() });

    fixture = TestBed.createComponent(GroupsListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function load(groups: VolunteerGroup[]): void {
    fixture.detectChanges();
    http.expectOne('/v1/volunteer-groups/groups').flush(groups);
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('offers a member with no application the wireframe action', () => {
    load([group()]);

    expect(component.standing(component.groups()[0]!)).toBe('Not a member');
    expect(component.action(component.groups()[0]!)).toBe('View / Apply');
  });

  it('says an application is with the president rather than offering to apply again', () => {
    load([group({ myApplicationStatus: 'Pending' })]);

    expect(component.standing(component.groups()[0]!)).toBe(
      'Your application is with the president',
    );
    expect(component.action(component.groups()[0]!)).toBe('View');
  });

  it('reports a rejected application honestly', () => {
    load([group({ myApplicationStatus: 'Rejected' })]);

    expect(component.standing(component.groups()[0]!)).toBe('Your application was not accepted');
  });

  it('tells a president they lead the group, not that they are a member', () => {
    // A president is also a member, so ordering matters: without it every
    // president would read "You are a member".
    load([group({ iAmThePresident: true, iAmAMember: true })]);

    expect(component.standing(component.groups()[0]!)).toBe('You lead this group');
  });

  it('does not offer to apply to a group that takes no new members', () => {
    load([group({ status: 'Inactive' })]);

    expect(component.action(component.groups()[0]!)).toBe('View');
    expect(text()).toContain('Not taking new members');
  });

  it('prompts a president about people waiting on them', () => {
    // The only prompt this platform has - there are no notifications yet, so
    // without it a queue sits unanswered.
    load([
      group({ id: 'a', iAmThePresident: true, iAmAMember: true, pendingApplicationCount: 2 }),
      group({ id: 'b', iAmThePresident: true, iAmAMember: true, pendingApplicationCount: 1 }),
      group({ id: 'c', pendingApplicationCount: 9 }),
    ]);

    // Only the groups they lead count towards it.
    expect(component.waitingOnMe()).toBe(3);
    expect(text()).toContain('3 people are waiting');
  });

  it('says nothing about waiting when a president has an empty queue', () => {
    load([group({ iAmThePresident: true, iAmAMember: true, pendingApplicationCount: 0 })]);

    expect(text()).not.toContain('waiting for you');
    expect(component.action(component.groups()[0]!)).toBe('Manage');
  });

  it('does not put a president name on the card it cannot resolve', () => {
    // The wireframe says "President: Rajesh Jain"; the group carries an id.
    load([group()]);

    expect(text()).not.toContain(PRESIDENT);
  });

  it('says the Samaaj has no groups rather than showing an empty grid', () => {
    load([]);

    expect(text()).toContain('not set up any volunteer groups');
  });

  it('offers a retry when the list cannot be loaded', () => {
    fixture.detectChanges();
    http
      .expectOne('/v1/volunteer-groups/groups')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();

    expect(text()).toContain('Try again');
  });
});

describe('GroupDetailComponent', () => {
  let fixture: ComponentFixture<GroupDetailComponent>;
  let component: GroupDetailComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [GroupDetailComponent],
      providers: [
        ...providers(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'g1' } } } },
      ],
    });

    fixture = TestBed.createComponent(GroupDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /**
   * Flushes every pending name-resolution call the component makes for a
   * member or applicant id it does not already know - see this component's
   * own doc comment on why it now resolves these instead of falling back to
   * "A member".
   */
  function flushMemberLookups(): void {
    for (const req of http.match((r) => r.method === 'GET' && r.url.startsWith('/v1/members/'))) {
      req.flush(apiMember(req.request.url.split('/').pop()!));
    }
  }

  /** Answers /me, the group and its elections; the queue only when the reader leads it. */
  function load(
    found: GroupDetail,
    applications: GroupApplication[] | null = null,
    elections: Election[] = [],
  ): void {
    fixture.detectChanges();

    http.expectOne('/v1/identity/me').flush(member);
    http.expectOne('/v1/volunteer-groups/groups/g1').flush(found);
    http.expectOne('/v1/volunteer-groups/groups/g1/elections').flush(elections);

    if (applications !== null) {
      http.expectOne('/v1/volunteer-groups/groups/g1/applications').flush(applications);
    }

    flushMemberLookups();
    fixture.detectChanges();
  }

  /** Each section of the page is a tab now; a test about one opens it first. */
  function showTab(name: 'about' | 'members' | 'applications' | 'roles' | 'elections'): void {
    component.tab.set(name);
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function buttonSaying(label: string): HTMLButtonElement | undefined {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((button) => button.textContent?.trim().startsWith(label)) as
      | HTMLButtonElement
      | undefined;
  }

  /** A button whose whole text is this - the roles table has "Edit Secretary" and "Remove Secretary" too. */
  function buttonExactly(label: string): HTMLButtonElement | undefined {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((button) => button.textContent?.trim() === label) as HTMLButtonElement | undefined;
  }

  // ---- Applying -----------------------------------------------------------

  it('offers the wireframe Apply button to somebody who may join', () => {
    load(detail());

    expect(component.canApply(component.detail()!)).toBe(true);
    expect(buttonSaying('Join group')).toBeDefined();
    expect(text()).toContain('No application submitted yet');
  });

  // ---- The page: a cover, a header and tabs --------------------------------

  it('opens on the group photo as a cover, fetched with the token', () => {
    load(detail({ photoUrl: '/v1/volunteer-groups/groups/g1/photo' }));

    http.expectOne('/v1/volunteer-groups/groups/g1/photo').flush(new Blob(['x'], { type: 'image/png' }));

    expect((fixture.nativeElement as HTMLElement).querySelector('img.group-cover-img')).not.toBeNull();
  });

  it('shows the group initials on the cover when there is no photo', () => {
    load(detail());

    const placeholder = (fixture.nativeElement as HTMLElement).querySelector('.group-cover-img.placeholder');

    expect(placeholder?.textContent?.trim()).toBe(component.initials(component.detail()!.group.name));
  });

  it('offers Applications as a tab only to whoever decides them', () => {
    load(detail());

    const tabs = () =>
      Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('[role="tab"]')).map((tab) =>
        tab.textContent?.trim(),
      );

    expect(tabs()).not.toContain('Applications');
    expect(tabs()[0]).toBe('About');
  });

  it("puts Join group in the header, and it opens the application on the About tab", () => {
    load(detail());
    showTab('members');

    buttonSaying('Join group')!.click();
    fixture.detectChanges();

    expect(component.tab()).toBe('about');
    expect((fixture.nativeElement as HTMLElement).querySelector('#apply-note')).not.toBeNull();
  });

  it('does not offer it to a member, a president, or an outstanding applicant', () => {
    load(detail({ iAmAMember: true }));
    expect(component.canApply(component.detail()!)).toBe(false);

    // A pending application blocks a second one; a rejected one does not,
    // because people ask again and a president can decide again.
    expect(component.canApply(detail({ myApplicationStatus: 'Pending' }))).toBe(false);
    expect(component.canApply(detail({ myApplicationStatus: 'Rejected' }))).toBe(true);
    expect(component.canApply(detail({ iAmThePresident: true }))).toBe(false);
    expect(component.canApply(detail({ status: 'Inactive' }))).toBe(false);
  });

  it('sends the note and re-reads the group, whose standing has just changed', () => {
    load(detail());

    component.showApply.set(true);
    component.note = '  I can help on Sundays.  ';
    component.apply();

    const request = http.expectOne('/v1/volunteer-groups/groups/g1/applications');

    expect(request.request.body).toEqual({ note: 'I can help on Sundays.' });

    request.flush(application());

    // Only the group is re-read: ensureCurrentUser caches, so /me is asked
    // once per screen rather than once per reload.
    http
      .expectOne('/v1/volunteer-groups/groups/g1')
      .flush(detail({ myApplicationStatus: 'Pending' }));
    http.expectOne('/v1/volunteer-groups/groups/g1/elections').flush([]);
    fixture.detectChanges();

    expect(text()).toContain('Waiting on the president');
  });

  it('sends no note rather than an empty one', () => {
    load(detail());

    component.showApply.set(true);
    component.note = '   ';
    component.apply();

    expect(http.expectOne('/v1/volunteer-groups/groups/g1/applications').request.body).toEqual({
      note: null,
    });
  });

  it('keeps a failed application off the page-level error', () => {
    load(detail());

    component.showApply.set(true);
    component.note = 'Please.';
    component.apply();

    http
      .expectOne('/v1/volunteer-groups/groups/g1/applications')
      .flush({ title: 'Group.Inactive', detail: 'No.' }, { status: 409, statusText: 'Conflict' });

    fixture.detectChanges();

    expect(component.error()).toBeNull();
    expect(component.applyError()).not.toBeNull();
    expect(text()).toContain('Seva Group');
  });

  // ---- The president's queue ----------------------------------------------

  it('does not ask for the queue when the reader does not lead the group', () => {
    // The endpoint answers 404 to anyone else; asking speculatively would mean
    // a 404 on every ordinary member's visit. http.verify() is the assertion.
    load(detail());

    expect(text()).not.toContain('Applications');
  });

  it('tells everybody a group has no president yet, and says how one gets set', () => {
    load(detail({ presidentMemberId: null, iAmAMember: true }));
    showTab('roles');

    expect(text()).toContain('This group has no president yet');
  });

  it('shows no such notice once a group has a president', () => {
    load(detail({ iAmAMember: true }));

    expect(text()).not.toContain('This group has no president yet');
  });

  it('gives an admin running a president-less group the queue, though they are not its president', () => {
    // iCanLead is the server's answer: the president, or an admin while there
    // is none. The screen does not re-derive it from a role.
    load(detail({ presidentMemberId: null, iAmThePresident: false, iCanLead: true }), [application()]);

    expect(component.pending().length).toBe(1);
    expect(text()).toContain('Applications');
  });

  it('picks the President role to start with for a group that has none', async () => {
    load(
      detail({ presidentMemberId: null, iAmThePresident: false, iCanLead: true, iCanRunElections: true }),
      [],
    );
    showTab('elections');

    component.openElectionForm(component.detail()!);
    fixture.detectChanges();
    // ngModel writes the value to the select a microtask later.
    await fixture.whenStable();

    const field = fixture.nativeElement.querySelector('#election-role') as HTMLSelectElement;
    expect(field.value).toBe('rp');
  });

  it('offers the president every role but Volunteer, and picks none for them', async () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), []);
    showTab('elections');

    component.openElectionForm(component.detail()!);
    fixture.detectChanges();
    await fixture.whenStable();

    const field = fixture.nativeElement.querySelector('#election-role') as HTMLSelectElement;
    expect(field.value).toBe('');
    expect([...field.options].map((o) => o.textContent!.trim())).toEqual([
      'Choose a role…',
      'President (1 seat)',
      'Secretary (1 seat)',
      'Coordinator (2 seats)',
    ]);
  });

  it('asks for it when they do, and shows only what is still waiting', () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), [
      application({ id: 'waiting' }),
      application({ id: 'done', status: 'Accepted', decidedBy: ME }),
    ]);

    // A decided application is not a queue item; showing it with Accept and
    // Turn down buttons would invite deciding it twice.
    expect(component.pending().map((a) => a.id)).toEqual(['waiting']);
  });

  it('accepting carries no position - that comes from an election, or a later assignment', () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), [application()]);
    showTab('applications');

    component.decide(component.pending()[0]!, true);

    const request = http.expectOne(
      '/v1/volunteer-groups/groups/g1/applications/a1/decide',
    );

    expect(request.request.body).toEqual({ accept: true });

    request.flush(application({ status: 'Accepted' }));

    // Accepting adds a member, so the group and the queue are both re-read.
    http
      .expectOne('/v1/volunteer-groups/groups/g1')
      .flush(detail({ iAmThePresident: true, iAmAMember: true, memberCount: 83 }));
    http.expectOne('/v1/volunteer-groups/groups/g1/elections').flush([]);
    http.expectOne('/v1/volunteer-groups/groups/g1/applications').flush([]);
    fixture.detectChanges();

    expect(text()).toContain('Nobody is waiting');
  });

  it('rejecting sends no position either', () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), [application()]);

    component.decide(component.pending()[0]!, false);

    expect(
      http.expectOne('/v1/volunteer-groups/groups/g1/applications/a1/decide').request.body,
    ).toEqual({ accept: false });
  });

  it('keeps a refused decision beside its own application', () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), [
      application({ id: 'a1' }),
      application({ id: 'a2' }),
    ]);

    component.decide(component.pending()[0]!, true);

    http
      .expectOne('/v1/volunteer-groups/groups/g1/applications/a1/decide')
      .flush({}, { status: 500, statusText: 'Server Error' });

    fixture.detectChanges();

    expect(component.error()).toBeNull();
    expect(component.decisionError()['a1']).toBeDefined();
    expect(component.decisionError()['a2']).toBeUndefined();

    // The other application is still decidable.
    expect(component.pending()).toHaveLength(2);
  });

  it('still shows the group when the queue cannot be loaded', () => {
    fixture.detectChanges();

    http.expectOne('/v1/identity/me').flush(member);
    http
      .expectOne('/v1/volunteer-groups/groups/g1')
      .flush(detail({ iAmThePresident: true, iAmAMember: true }));
    http.expectOne('/v1/volunteer-groups/groups/g1/elections').flush([]);
    http
      .expectOne('/v1/volunteer-groups/groups/g1/applications')
      .flush({}, { status: 500, statusText: 'Server Error' });

    fixture.detectChanges();

    expect(component.error()).toBeNull();
    expect(text()).toContain('Seva Group');
  });

  // ---- Roles ---------------------------------------------------------------

  it('lets a president move a member into a role, and back to Volunteer', () => {
    const members: GroupMember[] = [
      { memberId: 'm9', rolePosition: 'Volunteer', roleId: 'rv', joinedAt: new Date().toISOString() },
    ];
    load(detail({ iAmThePresident: true, iAmAMember: true }, members), []);

    component.assignRole('m9', 'rs');

    const request = http.expectOne('/v1/volunteer-groups/groups/g1/members/m9/role');

    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ roleId: 'rs' });
    request.flush(detail({ iAmThePresident: true, iAmAMember: true }, members));

    // Choosing the role a member already holds sends nothing.
    component.assignRole('m9', 'rv');
    http.expectNone('/v1/volunteer-groups/groups/g1/members/m9/role');
  });

  it('never offers the President role in the picker, nor a picker on the president', async () => {
    load(
      detail({ iAmThePresident: true, iAmAMember: true }, [
        { memberId: PRESIDENT, rolePosition: 'President', roleId: 'rp', joinedAt: new Date().toISOString() },
        { memberId: 'm9', rolePosition: 'Volunteer', roleId: 'rv', joinedAt: new Date().toISOString() },
      ]),
      [],
    );
    showTab('members');
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector(`#role-${PRESIDENT}`)).toBeNull();

    const select = fixture.nativeElement.querySelector('#role-m9') as HTMLSelectElement;

    expect([...select.options].map((o) => o.textContent!.trim())).toEqual([
      'Secretary',
      'Coordinator',
      'Volunteer',
    ]);
  });

  it('shows an ordinary member the roles but no way to change them', () => {
    load(detail({ iAmAMember: true }, [
      { memberId: 'm9', rolePosition: 'Secretary', roleId: 'rs', joinedAt: new Date().toISOString() },
    ]));
    showTab('roles');

    expect(text()).toContain('Volunteer roles');
    expect(text()).toContain('No limit');
    expect(fixture.nativeElement.querySelector('#role-m9')).toBeNull();
    expect(fixture.nativeElement.querySelector('#new-role-title')).toBeNull();
  });

  it('defines a role and takes the group the service answers with', () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), []);

    component.newRoleTitle = '  Treasurer ';
    component.newRoleSeats = 1;
    component.defineRole();

    const request = http.expectOne('/v1/volunteer-groups/groups/g1/roles');

    expect(request.request.body).toEqual({ title: 'Treasurer', seats: 1 });
    request.flush(detail({ iAmThePresident: true, iAmAMember: true }));

    expect(component.newRoleTitle).toBe('');
  });

  it('shows a refused role change and re-reads, since the picker already moved', () => {
    const members: GroupMember[] = [
      { memberId: 'm9', rolePosition: 'Volunteer', roleId: 'rv', joinedAt: new Date().toISOString() },
    ];
    load(detail({ iAmThePresident: true, iAmAMember: true }, members), []);

    component.assignRole('m9', 'rs');
    http
      .expectOne('/v1/volunteer-groups/groups/g1/members/m9/role')
      .flush({ title: 'Role.Full', detail: 'Every seat in this role is taken.' }, { status: 409, statusText: 'Conflict' });

    expect(component.roleError()).not.toBeNull();

    http.expectOne('/v1/volunteer-groups/groups/g1').flush(detail({ iAmThePresident: true, iAmAMember: true }, members));
    http.expectOne('/v1/volunteer-groups/groups/g1/elections').flush([]);
    http.expectOne('/v1/volunteer-groups/groups/g1/applications').flush([]);
  });

  // ---- Removing a member ---------------------------------------------------
  //
  // VolunteerGroup.RemoveMember existed and was called from nowhere: a
  // president could accept an application and give somebody a position, with
  // no way to undo either.

  it('offers Remove to the president, for an ordinary member', () => {
    load(
      detail({ iAmThePresident: true, iAmAMember: true }, [
        { memberId: 'm9', rolePosition: 'Volunteer', roleId: 'rv', joinedAt: new Date().toISOString() },
      ]),
      [],
    );
    showTab('members');

    expect(buttonSaying('Remove')).toBeDefined();
  });

  it('never offers it against the president themselves', () => {
    load(
      detail({ iAmThePresident: true, iAmAMember: true }, [
        { memberId: PRESIDENT, rolePosition: 'President', roleId: 'rp', joinedAt: new Date().toISOString() },
      ]),
      [],
    );

    // The service refuses this with a 409, and a button that always fails is
    // worse than no button.
    expect(buttonExactly('Remove')).toBeUndefined();
  });

  it('does not offer it to anyone but the president', () => {
    load(
      detail({ iAmThePresident: false, iAmAMember: true }, [
        { memberId: 'm9', rolePosition: 'Volunteer', roleId: 'rv', joinedAt: new Date().toISOString() },
      ]),
    );

    expect(buttonSaying('Remove')).toBeUndefined();
  });

  it('removes a member and re-reads the group', () => {
    load(
      detail({ iAmThePresident: true, iAmAMember: true }, [
        { memberId: 'm9', rolePosition: 'Volunteer', roleId: 'rv', joinedAt: new Date().toISOString() },
      ]),
      [],
    );

    component.removeMember('m9');

    const request = http.expectOne('/v1/volunteer-groups/groups/g1/members/m9');

    expect(request.request.method).toBe('DELETE');

    request.flush(detail({ iAmThePresident: true, iAmAMember: true }, []));

    expect(component.detail()!.members).toHaveLength(0);
  });

  it('shows a refusal rather than pretending it worked', () => {
    load(
      detail({ iAmThePresident: true, iAmAMember: true }, [
        { memberId: PRESIDENT, rolePosition: 'President', roleId: 'rp', joinedAt: new Date().toISOString() },
      ]),
      [],
    );

    component.removeMember(PRESIDENT);

    http.expectOne('/v1/volunteer-groups/groups/g1/members/' + PRESIDENT).flush(
      { title: 'Conflict', detail: 'The president cannot be removed from their own group.' },
      { status: 409, statusText: 'Conflict' },
    );

    fixture.detectChanges();

    expect(text()).toContain('The president cannot be removed from their own group');
  });

  // ---- Names -----------------------------------------------------------

  it('falls back to a generic label before a name has resolved', () => {
    const found = detail({ iAmThePresident: true, iAmAMember: true });

    load(found, []);

    expect(component.nameFor(ME, found)).toBe('You');
    expect(component.nameFor(PRESIDENT, found)).toBe('The president');
    expect(component.nameFor('somebody-else', found)).toBe('A member');
  });

  it('resolves an applicant own name and links it to their profile', () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), [
      application({ memberId: 'm9' }),
    ]);
    showTab('applications');

    expect(component.names()['m9']).toBe('Some Member');
    expect(text()).toContain('Some Member');

    const link = (fixture.nativeElement as HTMLElement).querySelector(
      'a[href="/members/m9"]',
    ) as HTMLAnchorElement | null;

    expect(link).toBeTruthy();
    expect(link?.textContent).toContain('Some Member');
  });

  it('resolves a group member own name too, linked to their profile, but never the readers own', () => {
    load(
      detail({ iAmThePresident: true, iAmAMember: true }, [
        { memberId: ME, rolePosition: 'Volunteer', roleId: 'rv', joinedAt: new Date().toISOString() },
        { memberId: 'm9', rolePosition: 'Volunteer', roleId: 'rv', joinedAt: new Date().toISOString() },
      ]),
      [],
    );
    showTab('members');

    expect(component.names()['m9']).toBe('Some Member');
    expect(component.names()['u1']).toBeUndefined();

    const links = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('a[href="/members/m9"]'),
    );

    expect(links).toHaveLength(1);
    expect((fixture.nativeElement as HTMLElement).querySelector('a[href="/members/u1"]')).toBeNull();
  });

  it('says there is no notification channel rather than implying one', () => {
    load(detail({ myApplicationStatus: 'Pending' }));

    expect(text()).toContain('check back here');
  });

  // ---- Elections ------------------------------------------------------------

  it('lists elections with their stage', () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), [], [election()]);
    showTab('elections');

    expect(text()).toContain('Secretary');
    expect(text()).toContain('Nominations open');
  });

  it('shows the winner once an election is published', () => {
    load(
      detail({ iAmThePresident: true, iAmAMember: true }, [
        { memberId: 'm9', rolePosition: 'Secretary', roleId: 'rs', joinedAt: new Date().toISOString() },
      ]),
      [],
      [election({ status: 'Published', winnerMemberIds: ['m9'], publishedAt: new Date().toISOString() })],
    );
    showTab('elections');

    expect(text()).toContain('Elected:');
    expect(text()).toContain('Some Member');
  });

  it('lets the president open an election for a position', () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), [], []);
    showTab('elections');

    component.showOpenElection.set(true);
    component.roleId = 'rs';
    component.nominationStartAt = '2026-04-01T00:00';
    component.nominationEndAt = '2026-04-08T00:00';
    component.votingStartAt = '2026-04-08T00:00';
    component.votingEndAt = '2026-04-15T00:00';
    component.openElection('g1');

    const request = http.expectOne('/v1/volunteer-groups/groups/g1/elections');

    expect(request.request.body.roleId).toBe('rs');

    request.flush(election());
    http.expectOne('/v1/volunteer-groups/groups/g1/elections').flush([election()]);
    fixture.detectChanges();

    expect(component.showOpenElection()).toBe(false);
    expect(text()).toContain('Secretary');
  });

  // ---- Correcting an election: reopening nominations that closed too early --

  it('offers Edit only while nominations can still be reopened', () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), [], [election({ status: 'Draft' })]);
    showTab('elections');

    expect(buttonSaying('Edit')).toBeDefined();

    expect(component.canEditElection(election({ status: 'NominationsOpen' }))).toBe(true);
    expect(component.canEditElection(election({ status: 'VotingOpen' }))).toBe(false);
    expect(component.canEditElection(election({ status: 'Closed' }))).toBe(false);
  });

  it('fills the edit form with the election as it stands', async () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), [], [election()]);
    showTab('elections');

    buttonExactly('Edit')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.editingElectionId()).toBe('e1');
    expect(component.editRoleId).toBe('rs');

    const select = fixture.nativeElement.querySelector('#edit-role-e1') as HTMLSelectElement;

    expect(select.value).toBe('rs');
  });

  it('sends the correction and reloads the elections', () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), [], [election()]);

    component.toggleEditElection(election());
    component.editNominationEndAt = '2026-09-01T00:00';
    component.editVotingStartAt = '2026-09-01T00:00';
    component.saveElectionEdit('e1');

    const request = http.expectOne(
      (r) => r.method === 'PATCH' && r.url === '/v1/volunteer-groups/elections/e1',
    );

    expect(request.request.body.roleId).toBe('rs');
    expect(request.request.body.nominationEndAt).toBe(new Date('2026-09-01T00:00').toISOString());

    request.flush(election({ nominationEndAt: request.request.body.nominationEndAt }));
    http.expectOne('/v1/volunteer-groups/groups/g1/elections').flush([election()]);
    fixture.detectChanges();

    expect(component.editingElectionId()).toBeNull();
  });

  it('keeps the form open and shows the refusal once voting has opened', () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), [], [election({ status: 'VotingOpen' })]);
    showTab('elections');

    // canEditElection already hides the button in the template; calling the
    // handler directly exercises what the service itself would refuse.
    component.toggleEditElection(election({ status: 'VotingOpen' }));
    component.saveElectionEdit('e1');

    http
      .expectOne((r) => r.method === 'PATCH')
      .flush({ title: 'Election.NotEditable' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(component.editingElectionId()).toBe('e1');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
  });

  it('explains, rather than showing a blank picker, when nobody has joined the group yet', () => {
    // A group created with no president has no members, and only a member can
    // nominate or be nominated - so the picker would be empty for the one person
    // who reached this screen to run the election.
    load(detail({ iAmThePresident: false, iAmAMember: false, iCanLead: true }, []), [], [election()]);
    showTab('elections');

    component.toggleElection('e1');
    http.expectOne('/v1/volunteer-groups/elections/e1').flush(electionDetail());
    fixture.detectChanges();

    expect(text()).toContain('Only members of this group can nominate');
    expect(text()).toContain('Nobody has joined yet');
    expect(text()).toContain('you accept them in Applications above');
    expect(document.body.querySelector('select[id^="nominee-"]')).toBeNull();
  });

  it('tells a member of the Samaaj who is not in the group to ask to join, with no picker', () => {
    load(
      detail({ iAmThePresident: false, iAmAMember: false, iCanLead: false }, [
        { memberId: PRESIDENT, rolePosition: 'President', roleId: 'rp', joinedAt: new Date().toISOString() },
      ]),
      null,
      [election()],
    );
    showTab('elections');

    component.toggleElection('e1');
    http.expectOne('/v1/volunteer-groups/elections/e1').flush(electionDetail());
    fixture.detectChanges();

    expect(text()).toContain('Ask to join this group to take part');
    expect(document.body.querySelector('select[id^="nominee-"]')).toBeNull();
  });

  // ---- Why there is no vote button ---------------------------------------------

  const PAST = new Date(Date.now() - 2 * 86400000).toISOString();

  function openBallot(
    groupOverrides: Partial<VolunteerGroup>,
    electionOverrides: Partial<Election>,
    candidates: ElectionCandidate[],
  ): void {
    load(
      detail({ iAmAMember: true, ...groupOverrides }, [
        { memberId: PRESIDENT, rolePosition: 'President', roleId: 'rp', joinedAt: new Date().toISOString() },
        { memberId: 'm9', rolePosition: 'Volunteer', roleId: 'rv', joinedAt: new Date().toISOString() },
      ]),
      groupOverrides.iCanLead ? [] : null,
      [election(electionOverrides)],
    );

    showTab('elections');
    component.toggleElection('e1');
    http.expectOne('/v1/volunteer-groups/elections/e1').flush(electionDetail(electionOverrides, candidates));
    fixture.detectChanges();
  }

  it('shows a vote button when voting is open and somebody is on the ballot', () => {
    openBallot({}, { status: 'VotingOpen', acceptsVotes: true, acceptsNominations: false, votingStartAt: PAST },
      [candidate({ status: 'Approved' })]);

    expect(text()).toContain('Vote');
    expect(text()).not.toContain('Voting has');
  });

  it('takes a ballot of up to the seats for a role with several, and casts it once', () => {
    const open = {
      status: 'VotingOpen' as const,
      acceptsVotes: true,
      acceptsNominations: false,
      votingStartAt: PAST,
      seats: 2,
    };
    openBallot({}, open, [
      candidate({ id: 'c1', memberId: 'x1', status: 'Approved' }),
      candidate({ id: 'c2', memberId: 'x2', status: 'Approved' }),
      candidate({ id: 'c3', memberId: 'x3', status: 'Approved' }),
    ]);
    http.match((r) => r.url.startsWith('/v1/members/')).forEach((r) => r.flush({}));

    expect(text()).toContain('Pick up to 2');
    expect(text()).toContain('It is final');
    expect(buttonSaying('Vote')).toBeUndefined();

    component.togglePick('e1', 'c1');
    component.togglePick('e1', 'c3');
    fixture.detectChanges();

    // A third tick is refused by the checkboxes themselves once the seats are full.
    const boxes = [...fixture.nativeElement.querySelectorAll('input[type="checkbox"]')] as HTMLInputElement[];

    expect(boxes.filter((b) => b.disabled)).toHaveLength(1);

    buttonSaying('Cast my ballot (2 of 2)')!.click();

    const request = http.expectOne('/v1/volunteer-groups/elections/e1/votes');

    expect(request.request.body).toEqual({ candidateIds: ['c1', 'c3'] });
    request.flush({ electionId: 'e1', candidateIds: ['c1', 'c3'], accepted: true });
    http.expectOne('/v1/volunteer-groups/groups/g1/elections').flush([election(open)]);
    http.expectOne('/v1/volunteer-groups/elections/e1').flush(electionDetail(open, []));

    expect(component.picks('e1')).toEqual([]);
  });

  it('says the voting window has not started when the status is on but the clock is not', () => {
    // Status VotingOpen with a start date still ahead: acceptsVotes is false, the
    // button is absent, and this used to give no reason at all.
    openBallot({}, { status: 'VotingOpen', acceptsVotes: false, acceptsNominations: false },
      [candidate({ status: 'Approved' })]);

    expect(text()).toContain('switched on, but its window does not start until');
    expect(text()).toContain('Voting opens soon');
  });

  it('says voting has not been opened, and tells the organiser what to press', () => {
    openBallot({ iCanRunElections: true }, { status: 'NominationsOpen', acceptsVotes: false },
      [candidate({ status: 'Approved' })]);

    expect(text()).toContain('Voting has not been opened yet');
    expect(text()).toContain('use "Open voting" above');
  });

  it('tells a member the organiser opens voting', () => {
    openBallot({}, { status: 'NominationsOpen', acceptsVotes: false }, [candidate({ status: 'Approved' })]);

    expect(text()).toContain('The organiser opens it once nominations are done');
  });

  it('says nobody is on the ballot when voting is open but no nomination was approved', () => {
    openBallot({}, { status: 'VotingOpen', acceptsVotes: true, acceptsNominations: false, votingStartAt: PAST },
      [candidate({ status: 'Nominated' })]);

    expect(text()).toContain('nobody is on the ballot yet');
  });

  it('says only members can vote to somebody outside the group', () => {
    openBallot({ iAmAMember: false, iAmThePresident: false },
      { status: 'VotingOpen', acceptsVotes: true, acceptsNominations: false, votingStartAt: PAST },
      [candidate({ status: 'Approved' })]);

    expect(text()).toContain('Only members of this group can vote');
  });

  it('says nothing once the reader has voted', () => {
    openBallot({}, { status: 'VotingOpen', acceptsVotes: true, votingStartAt: PAST, myVoteCandidateIds: ['c1'] },
      [candidate({ status: 'Approved' })]);

    expect(text()).toContain('Your vote');
    expect(text()).not.toContain('Voting has');
  });

  it('expands a ballot, nominates a group member, and the president approves it', () => {
    load(
      detail({ iAmThePresident: true, iAmAMember: true }, [
        { memberId: PRESIDENT, rolePosition: 'President', roleId: 'rp', joinedAt: new Date().toISOString() },
        { memberId: 'm9', rolePosition: 'Volunteer', roleId: 'rv', joinedAt: new Date().toISOString() },
      ]),
      [],
      [election()],
    );

    component.toggleElection('e1');
    http.expectOne('/v1/volunteer-groups/elections/e1').flush(electionDetail());
    fixture.detectChanges();

    component.setNomineeDraft('e1', 'm9');
    component.nominate('e1');

    const nominateRequest = http.expectOne('/v1/volunteer-groups/elections/e1/candidates');

    expect(nominateRequest.request.body).toEqual({ memberId: 'm9' });

    nominateRequest.flush({ electionId: 'e1', candidateId: 'c1', memberId: 'm9', nominated: true });
    http
      .expectOne('/v1/volunteer-groups/elections/e1')
      .flush(electionDetail({}, [candidate({ status: 'Nominated' })]));
    fixture.detectChanges();

    expect(text()).toContain('Some Member');

    component.decideCandidate('e1', 'c1', true);

    const decideRequest = http.expectOne('/v1/volunteer-groups/elections/e1/candidates/c1/decide');

    expect(decideRequest.request.body).toEqual({ approve: true });

    decideRequest.flush(electionDetail({}, [candidate({ status: 'Approved' })]));
    fixture.detectChanges();

    expect(component.approvedCandidates(component.expandedDetail()!)).toHaveLength(1);
  });

  it('casts a vote and reflects the tally', () => {
    load(
      detail({ iAmThePresident: true, iAmAMember: true }, [
        { memberId: PRESIDENT, rolePosition: 'President', roleId: 'rp', joinedAt: new Date().toISOString() },
        { memberId: 'm9', rolePosition: 'Volunteer', roleId: 'rv', joinedAt: new Date().toISOString() },
      ]),
      [],
      [election({ status: 'VotingOpen', acceptsNominations: false, acceptsVotes: true })],
    );
    showTab('elections');

    component.toggleElection('e1');
    http
      .expectOne('/v1/volunteer-groups/elections/e1')
      .flush(electionDetail(
        { status: 'VotingOpen', acceptsNominations: false, acceptsVotes: true },
        [candidate({ status: 'Approved' })],
      ));
    fixture.detectChanges();

    component.castVote('e1', ['c1']);

    const voteRequest = http.expectOne('/v1/volunteer-groups/elections/e1/votes');

    expect(voteRequest.request.body).toEqual({ candidateIds: ['c1'] });

    voteRequest.flush({ electionId: 'e1', candidateIds: ['c1'], accepted: true });
    http
      .expectOne('/v1/volunteer-groups/groups/g1/elections')
      .flush([election({ status: 'VotingOpen', acceptsNominations: false, acceptsVotes: true, myVoteCandidateIds: ['c1'] })]);
    http
      .expectOne('/v1/volunteer-groups/elections/e1')
      .flush(electionDetail(
        { status: 'VotingOpen', acceptsNominations: false, acceptsVotes: true, myVoteCandidateIds: ['c1'] },
        [candidate({ status: 'Approved', votes: 1 })],
      ));
    fixture.detectChanges();

    expect(text()).toContain('Your vote');
  });
});
