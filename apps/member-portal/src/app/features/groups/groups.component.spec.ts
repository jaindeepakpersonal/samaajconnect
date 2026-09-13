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
  VolunteerGroup,
} from './groups.models';
import { Member } from '../members/members.models';

const ME = 'u1';
const PRESIDENT = 'p1';

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
  };
}

function detail(
  overrides: Partial<VolunteerGroup> = {},
  members: GroupMember[] = [],
): GroupDetail {
  return { group: group(overrides), members };
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
    myVoteCandidateId: null,
    candidateCount: 0,
    winnerMemberId: null,
    publishedAt: null,
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

  // ---- Applying -----------------------------------------------------------

  it('offers the wireframe Apply button to somebody who may join', () => {
    load(detail());

    expect(component.canApply(component.detail()!)).toBe(true);
    expect(buttonSaying('Apply to join')).toBeDefined();
    expect(text()).toContain('No application submitted yet');
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

  // ---- Positions -----------------------------------------------------------

  it('lets a president set and clear a position', () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), []);

    component.editPosition('m9', null);
    component.positionDraft = 'Secretary';
    component.savePosition('m9');

    const request = http.expectOne('/v1/volunteer-groups/groups/g1/members/m9/position');

    expect(request.request.body).toEqual({ rolePosition: 'Secretary' });

    request.flush(detail({ iAmThePresident: true, iAmAMember: true }));

    expect(component.editingPosition()).toBeNull();
  });

  it('clears a position by sending null rather than an empty string', () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), []);

    component.editPosition('m9', 'Secretary');
    component.positionDraft = '  ';
    component.savePosition('m9');

    expect(
      http.expectOne('/v1/volunteer-groups/groups/g1/members/m9/position').request.body,
    ).toEqual({ rolePosition: null });
  });

  // ---- Removing a member ---------------------------------------------------
  //
  // VolunteerGroup.RemoveMember existed and was called from nowhere: a
  // president could accept an application and give somebody a position, with
  // no way to undo either.

  it('offers Remove to the president, for an ordinary member', () => {
    load(
      detail({ iAmThePresident: true, iAmAMember: true }, [
        { memberId: 'm9', rolePosition: null, joinedAt: new Date().toISOString() },
      ]),
      [],
    );

    expect(buttonSaying('Remove')).toBeDefined();
  });

  it('never offers it against the president themselves', () => {
    load(
      detail({ iAmThePresident: true, iAmAMember: true }, [
        { memberId: PRESIDENT, rolePosition: 'President', joinedAt: new Date().toISOString() },
      ]),
      [],
    );

    // The service refuses this with a 409, and a button that always fails is
    // worse than no button.
    expect(buttonSaying('Remove')).toBeUndefined();
  });

  it('does not offer it to anyone but the president', () => {
    load(
      detail({ iAmThePresident: false, iAmAMember: true }, [
        { memberId: 'm9', rolePosition: null, joinedAt: new Date().toISOString() },
      ]),
    );

    expect(buttonSaying('Remove')).toBeUndefined();
  });

  it('removes a member and re-reads the group', () => {
    load(
      detail({ iAmThePresident: true, iAmAMember: true }, [
        { memberId: 'm9', rolePosition: null, joinedAt: new Date().toISOString() },
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
        { memberId: PRESIDENT, rolePosition: 'President', joinedAt: new Date().toISOString() },
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
        { memberId: ME, rolePosition: null, joinedAt: new Date().toISOString() },
        { memberId: 'm9', rolePosition: null, joinedAt: new Date().toISOString() },
      ]),
      [],
    );

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

    expect(text()).toContain('Secretary');
    expect(text()).toContain('Nominations open');
  });

  it('shows the winner once an election is published', () => {
    load(
      detail({ iAmThePresident: true, iAmAMember: true }, [
        { memberId: 'm9', rolePosition: 'Secretary', joinedAt: new Date().toISOString() },
      ]),
      [],
      [election({ status: 'Published', winnerMemberId: 'm9', publishedAt: new Date().toISOString() })],
    );

    expect(text()).toContain('Elected:');
    expect(text()).toContain('Some Member');
  });

  it('lets the president open an election for a position', () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), [], []);

    component.showOpenElection.set(true);
    component.positionTitle = 'Secretary';
    component.nominationStartAt = '2026-04-01T00:00';
    component.nominationEndAt = '2026-04-08T00:00';
    component.votingStartAt = '2026-04-08T00:00';
    component.votingEndAt = '2026-04-15T00:00';
    component.openElection('g1');

    const request = http.expectOne('/v1/volunteer-groups/groups/g1/elections');

    expect(request.request.body.positionTitle).toBe('Secretary');

    request.flush(election());
    http.expectOne('/v1/volunteer-groups/groups/g1/elections').flush([election()]);
    fixture.detectChanges();

    expect(component.showOpenElection()).toBe(false);
    expect(text()).toContain('Secretary');
  });

  it('expands a ballot, nominates a group member, and the president approves it', () => {
    load(
      detail({ iAmThePresident: true, iAmAMember: true }, [
        { memberId: PRESIDENT, rolePosition: 'President', joinedAt: new Date().toISOString() },
        { memberId: 'm9', rolePosition: null, joinedAt: new Date().toISOString() },
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
        { memberId: PRESIDENT, rolePosition: 'President', joinedAt: new Date().toISOString() },
        { memberId: 'm9', rolePosition: null, joinedAt: new Date().toISOString() },
      ]),
      [],
      [election({ status: 'VotingOpen', acceptsNominations: false, acceptsVotes: true })],
    );

    component.toggleElection('e1');
    http
      .expectOne('/v1/volunteer-groups/elections/e1')
      .flush(electionDetail(
        { status: 'VotingOpen', acceptsNominations: false, acceptsVotes: true },
        [candidate({ status: 'Approved' })],
      ));
    fixture.detectChanges();

    component.castVote('e1', 'c1');

    const voteRequest = http.expectOne('/v1/volunteer-groups/elections/e1/votes');

    expect(voteRequest.request.body).toEqual({ candidateId: 'c1' });

    voteRequest.flush({ electionId: 'e1', candidateId: 'c1', accepted: true });
    http
      .expectOne('/v1/volunteer-groups/groups/g1/elections')
      .flush([election({ status: 'VotingOpen', acceptsNominations: false, acceptsVotes: true, myVoteCandidateId: 'c1' })]);
    http
      .expectOne('/v1/volunteer-groups/elections/e1')
      .flush(electionDetail(
        { status: 'VotingOpen', acceptsNominations: false, acceptsVotes: true, myVoteCandidateId: 'c1' },
        [candidate({ status: 'Approved', votes: 1 })],
      ));
    fixture.detectChanges();

    expect(text()).toContain('Your vote');
  });
});
