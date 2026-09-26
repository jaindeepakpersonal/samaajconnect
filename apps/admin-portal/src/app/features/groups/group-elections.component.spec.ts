import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import {
  GroupApplication,
  GroupDetail,
  GroupElection,
  GroupElectionCandidate,
  GroupElectionDetail,
  GroupRole,
  LedGroup,
} from '../../core/admin.models';
import { GroupElectionsComponent } from './group-elections.component';

const GROUP_ID = 'g1';

const ROLES: GroupRole[] = [
  { id: 'rp', title: 'President', kind: 'President', seats: 1, holderCount: 0, isEditable: false, isElected: true },
  { id: 'rs', title: 'Secretary', kind: 'Elected', seats: 2, holderCount: 1, isEditable: true, isElected: true },
  { id: 'rv', title: 'Volunteer', kind: 'Volunteer', seats: null, holderCount: 2, isEditable: false, isElected: false },
];

function group(overrides: Partial<LedGroup> = {}): LedGroup {
  return {
    id: GROUP_ID,
    name: 'Seva Sangh',
    description: null,
    focusArea: null,
    localityId: null,
    presidentMemberId: null,
    status: 'Active',
    memberCount: 3,
    iAmThePresident: false,
    iCanLead: true,
    iCanRunElections: true,
    ...overrides,
  };
}

function detail(overrides: Partial<LedGroup> = {}): GroupDetail {
  return {
    group: group(overrides),
    members: [
      { memberId: 'm1', rolePosition: 'Volunteer', roleId: 'rv', joinedAt: '2026-01-01T00:00:00Z' },
      { memberId: 'm2', rolePosition: 'Volunteer', roleId: 'rv', joinedAt: '2026-01-01T00:00:00Z' },
    ],
    roles: ROLES,
  };
}

function election(overrides: Partial<GroupElection> = {}): GroupElection {
  return {
    id: 'e1',
    groupId: GROUP_ID,
    positionTitle: 'President',
    nominationStartAt: '2026-08-01T00:00:00Z',
    nominationEndAt: '2026-08-20T00:00:00Z',
    votingStartAt: '2026-09-01T00:00:00Z',
    votingEndAt: '2026-09-20T00:00:00Z',
    status: 'Draft',
    acceptsNominations: false,
    acceptsVotes: false,
    candidateCount: 0,
    winnerMemberIds: [],
    publishedAt: null,
    createdAt: '2026-07-01T00:00:00Z',
    roleId: 'rp',
    seats: 1,
    ...overrides,
  };
}

function candidate(overrides: Partial<GroupElectionCandidate> = {}): GroupElectionCandidate {
  return { id: 'c1', memberId: 'm1', status: 'Nominated', nominatedBy: 'm2', votes: 0, ...overrides };
}

function application(overrides: Partial<GroupApplication> = {}): GroupApplication {
  return {
    id: 'a1',
    memberId: 'm1',
    note: 'I would like to help.',
    status: 'Pending',
    createdAt: '2026-07-02T00:00:00Z',
    ...overrides,
  };
}

const MEMBERS = [
  { id: 'm1', fullName: 'Diya Jain' },
  { id: 'm2', fullName: 'Aarav Jain' },
];

describe('GroupElectionsComponent', () => {
  let fixture: ComponentFixture<GroupElectionsComponent>;
  let component: GroupElectionsComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [GroupElectionsComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => GROUP_ID } } } },
      ],
    });

    fixture = TestBed.createComponent(GroupElectionsComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  const buttonSaying = (label: string) =>
    [...fixture.nativeElement.querySelectorAll('button')].find((b) =>
      (b as HTMLElement).textContent?.trim().startsWith(label),
    ) as HTMLButtonElement | undefined;

  const GROUP_URL = `/v1/volunteer-groups/groups/${GROUP_ID}`;

  function load(
    found: GroupDetail = detail(),
    elections: GroupElection[] = [],
    applications: GroupApplication[] = [],
  ) {
    fixture.detectChanges();
    http.expectOne(GROUP_URL).flush(found);
    http.expectOne(`${GROUP_URL}/elections`).flush(elections);
    answerApplications(found, applications);
    http.expectOne('/v1/members?take=100').flush({ items: MEMBERS, hasMore: false });
    fixture.detectChanges();
  }

  /** The queue is asked for only when the service said the caller may decide it. */
  function answerApplications(found: GroupDetail, applications: GroupApplication[] = []) {
    if (found.group.iCanLead) {
      http.expectOne(`${GROUP_URL}/applications`).flush(applications);
    } else {
      http.expectNone(`${GROUP_URL}/applications`);
    }
  }

  /** What a successful action does next: re-read the group and its elections. */
  function reload(found: GroupDetail = detail(), elections: GroupElection[] = []) {
    http.expectOne(GROUP_URL).flush(found);
    http.expectOne(`${GROUP_URL}/elections`).flush(elections);
    answerApplications(found);
    fixture.detectChanges();
  }

  function ballot(election: GroupElection, candidates: GroupElectionCandidate[]) {
    const body: GroupElectionDetail = { election, candidates };

    component.toggle(election);
    http.expectOne(`/v1/volunteer-groups/elections/${election.id}`).flush(body);
    fixture.detectChanges();
  }

  it('explains a 404 as no such group rather than as an error', () => {
    fixture.detectChanges();
    http.expectOne(GROUP_URL).flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('No such group');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('tells an administrator a president-less group is theirs to run an election for', () => {
    load(detail({ presidentMemberId: null, iCanLead: true }));

    expect(text()).toContain('This group has no president');
    expect(buttonSaying('Open an election')).toBeDefined();
  });

  it('lets an administrator run elections after the group has a president', () => {
    // Changed 2026-09-26: an administrator runs any role's election at any
    // time. Join requests are still the president's, so the queue is not asked for.
    load(detail({ presidentMemberId: 'm1', iCanLead: false, iCanRunElections: true }));

    expect(buttonSaying('Open an election')).toBeDefined();
    expect(text()).not.toContain('Join requests');
  });

  it('offers no way to open an election to somebody who cannot run them', () => {
    // The service refuses it, so the screen does not draw a control that answers 404.
    load(detail({ presidentMemberId: 'm1', iCanLead: false, iCanRunElections: false }));

    expect(text()).toContain('Diya Jain is this group');
    expect(buttonSaying('Open')).toBeUndefined();
  });

  it('picks the President role to start with in a group with none, and offers no Volunteer election', async () => {
    load();

    component.toggleOpenForm(component.detail()!);
    fixture.detectChanges();
    await fixture.whenStable();

    const select = fixture.nativeElement.querySelector('#election-role') as HTMLSelectElement;
    const options = [...select.options].map((o) => o.textContent!.trim());

    expect(component.roleId).toBe('rp');
    expect(options).toContain('President (1 seat)');
    expect(options).toContain('Secretary (2 seats)');
    expect(options.some((o) => o.startsWith('Volunteer'))).toBe(false);
  });

  it('refuses a voting window that starts before nominations close', () => {
    load();

    component.roleId = 'rp';
    component.nominationStartAt = '2026-08-01T00:00';
    component.nominationEndAt = '2026-08-20T00:00';
    component.votingStartAt = '2026-08-15T00:00';
    component.votingEndAt = '2026-09-20T00:00';

    expect(component.windowsOverlap()).toBe(true);
    expect(component.canOpen()).toBe(false);

    component.open(component.detail()!);
    http.expectNone(`${GROUP_URL}/elections`);
  });

  it('opens the election as a draft and re-reads', () => {
    load();

    component.roleId = 'rp';
    component.nominationStartAt = '2026-08-01T00:00';
    component.nominationEndAt = '2026-08-20T00:00';
    component.votingStartAt = '2026-08-20T00:00';
    component.votingEndAt = '2026-09-20T00:00';

    component.open(component.detail()!);

    const request = http.expectOne((r) => r.method === 'POST' && r.url === `${GROUP_URL}/elections`);

    expect(request.request.body.roleId).toBe('rp');
    request.flush(election());
    reload(detail(), [election()]);

    expect(text()).toContain('The President election is set up as a draft');
    expect(text()).toContain('Not started');
  });

  // ---- Correcting one: reopening nominations that closed too early ---------

  it('offers Edit only while the election can still be edited', () => {
    load(detail(), [
      election({ id: 'e1', status: 'Draft' }),
      election({ id: 'e2', status: 'NominationsOpen' }),
      election({ id: 'e3', status: 'VotingOpen' }),
      election({ id: 'e4', status: 'Closed' }),
    ]);

    expect(component.canEditElection(election({ status: 'Draft' }))).toBe(true);
    expect(component.canEditElection(election({ status: 'NominationsOpen' }))).toBe(true);
    expect(component.canEditElection(election({ status: 'VotingOpen' }))).toBe(false);
    expect(component.canEditElection(election({ status: 'Closed' }))).toBe(false);
  });

  it('gives somebody who cannot run elections no Edit', () => {
    load(detail({ presidentMemberId: 'm1', iCanLead: false, iCanRunElections: false }), [
      election({ positionTitle: 'Secretary', status: 'Draft' }),
    ]);

    expect(buttonSaying('Edit')).toBeUndefined();
  });

  it('fills the edit form with the election as it stands', async () => {
    load(detail(), [
      election({
        positionTitle: 'President',
        nominationStartAt: '2026-08-01T09:00:00Z',
        nominationEndAt: '2026-08-20T09:00:00Z',
      }),
    ]);

    // The election's own Edit, not a role's - the roles table has its own.
    const editButtons = [...fixture.nativeElement.querySelectorAll('.election button')].filter(
      (b) => (b as HTMLElement).textContent?.trim() === 'Edit',
    ) as HTMLButtonElement[];
    editButtons[0]!.click();
    fixture.detectChanges();

    // NgModel's initial write to the control is deferred a microtask, the same
    // reason the member portal's own async ngModel specs await this.
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.editRoleId).toBe('rp');
    expect(component.editingId()).toBe('e1');

    const select = fixture.nativeElement.querySelector('#edit-role-e1') as HTMLSelectElement;

    expect(select.value).toBe('rp');
  });

  it('sends the correction and re-reads', () => {
    load(detail(), [election()]);

    component.toggleEditForm(election());
    component.editNominationEndAt = '2026-09-01T00:00';
    component.editVotingStartAt = '2026-09-01T00:00';
    component.saveEdit(election());

    const request = http.expectOne(
      (r) => r.method === 'PATCH' && r.url === '/v1/volunteer-groups/elections/e1',
    );

    expect(request.request.body.roleId).toBe('rp');
    expect(request.request.body.nominationEndAt).toBe(new Date('2026-09-01T00:00').toISOString());

    request.flush(election({ nominationEndAt: request.request.body.nominationEndAt }));
    reload();

    expect(text()).toContain('Election updated');
    expect(component.editingId()).toBeNull();
  });

  it('refuses a corrected voting window that starts before nominations close', () => {
    load(detail(), [election()]);

    component.toggleEditForm(election());
    component.editNominationEndAt = '2026-08-20T00:00';
    component.editVotingStartAt = '2026-08-15T00:00';

    expect(component.editWindowsOverlap()).toBe(true);
    expect(component.canSaveEdit()).toBe(false);

    component.saveEdit(election());
    http.expectNone((r) => r.method === 'PATCH');
  });

  it('keeps the form open and shows the refusal once voting has opened', () => {
    load(detail(), [election({ status: 'VotingOpen' })]);

    // canEditElection already hides the button in the template; a direct call
    // exercises what the service itself would refuse.
    component.toggleEditForm(election({ status: 'VotingOpen' }));
    component.saveEdit(election({ status: 'VotingOpen' }));

    http
      .expectOne((r) => r.method === 'PATCH')
      .flush({ title: 'Election.NotEditable' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
  });

  it('offers the one next stage, and moves it', () => {
    load(detail(), [election({ status: 'Draft' })]);

    expect(component.nextStatus(election({ status: 'Draft' }))).toBe('NominationsOpen');

    buttonSaying('Open nominations')!.click();

    const request = http.expectOne(`/v1/volunteer-groups/elections/e1/status`);

    expect(request.request.body).toEqual({ status: 'NominationsOpen' });
    request.flush(election({ status: 'NominationsOpen' }));
    reload(detail(), [election({ status: 'NominationsOpen' })]);
  });

  it('gives somebody who cannot run elections no controls on one', () => {
    load(detail({ presidentMemberId: 'm1', iCanLead: false, iCanRunElections: false }), [
      election({ positionTitle: 'Secretary', status: 'Draft' }),
    ]);

    expect(buttonSaying('Open nominations')).toBeUndefined();
    expect(buttonSaying('View ballot')).toBeDefined();
  });

  it('shows the ballot with its votes and names', () => {
    const e = election({ status: 'VotingOpen' });

    load(detail(), [e]);
    ballot(e, [candidate({ status: 'Approved', votes: 4 })]);

    expect(text()).toContain('Diya Jain');
    expect(text()).toContain('Aarav Jain');
    expect(text()).toContain('On the ballot');
    expect(text()).toContain('4');
  });

  it('approves a nomination onto the ballot', () => {
    const e = election({ status: 'NominationsOpen' });

    load(detail(), [e]);
    ballot(e, [candidate()]);

    buttonSaying('Approve')!.click();

    const request = http.expectOne('/v1/volunteer-groups/elections/e1/candidates/c1/decide');

    expect(request.request.body).toEqual({ approve: true });
    request.flush({ election: e, candidates: [candidate({ status: 'Approved' })] });
    reload(detail(), [e]);
    http.expectOne('/v1/volunteer-groups/elections/e1').flush({ election: e, candidates: [] });
  });

  it('stops offering to change the ballot once voting has opened', () => {
    const e = election({ status: 'VotingOpen' });

    load(detail(), [e]);
    ballot(e, [candidate({ status: 'Approved', votes: 1 })]);

    expect(buttonSaying('Approve')).toBeUndefined();
    expect(buttonSaying('Take off the ballot')).toBeUndefined();
  });

  it('will not publish on one click, and says the winner takes the position', () => {
    const e = election({ status: 'Closed' });

    load(detail(), [e]);

    buttonSaying('Publish the result')!.click();
    fixture.detectChanges();

    expect(text()).toContain('installs them as');
    http.expectNone(`/v1/volunteer-groups/elections/e1/publish`);
  });

  it('publishes once confirmed, then re-reads the group', () => {
    const e = election({ status: 'Closed' });

    load(detail(), [e]);
    component.publish(e);

    const request = http.expectOne('/v1/volunteer-groups/elections/e1/publish');

    request.flush(election({ status: 'Published', winnerMemberIds: ['m1'] }));
    reload(
      detail({ presidentMemberId: 'm1', iCanLead: false }),
      [election({ status: 'Published', winnerMemberIds: ['m1'] })],
    );

    expect(text()).toContain('winners now hold the role');
    expect(text()).toContain('Elected:');
    expect(text()).toContain('Diya Jain');
  });

  it('names every winner of a role with more than one seat', () => {
    load(detail(), [
      election({ positionTitle: 'Secretary', roleId: 'rs', seats: 2, status: 'Published', winnerMemberIds: ['m1', 'm2'] }),
    ]);

    expect(text()).toContain('Elected: Diya Jain, Aarav Jain');
    expect(text()).toContain('2 seats');
  });

  // ---- Roles ---------------------------------------------------------------

  it('lists the roles with their seats and holders', () => {
    load();

    expect(text()).toContain('Volunteer roles');
    expect(text()).toContain('No limit');
    expect(text()).toContain("everybody's until elected to another");
  });

  it('defines a role and re-reads', () => {
    load();

    component.newRoleTitle = '  Treasurer ';
    component.newRoleSeats = 1;
    component.defineRole(component.detail()!);

    const request = http.expectOne((r) => r.method === 'POST' && r.url === `${GROUP_URL}/roles`);

    expect(request.request.body).toEqual({ title: 'Treasurer', seats: 1 });
    request.flush(detail());
    reload();

    expect(text()).toContain('Treasurer is a role in Seva Sangh now, with 1 seat');
  });

  it('refuses a role with no title or no seats before asking', () => {
    load();

    expect(component.validRole('', 1)).toBe(false);
    expect(component.validRole('Treasurer', 0)).toBe(false);
    expect(component.validRole('Treasurer', 1.5)).toBe(false);
    expect(component.validRole('Treasurer', 3)).toBe(true);
  });

  it('offers no edit or removal of the built-in roles, and no removal of a held one', () => {
    load();

    // Only Secretary is editable; it has a holder, so Remove is drawn disabled.
    const remove = buttonSaying('Remove') as HTMLButtonElement;

    expect(remove).toBeDefined();
    expect(remove.disabled).toBe(true);
    expect(
      [...fixture.nativeElement.querySelectorAll('button')].filter((b) =>
        (b as HTMLElement).textContent?.trim().startsWith('Remove'),
      ).length,
    ).toBe(1);
  });

  it('moves a member into a role from the roster, but never offers President', async () => {
    load(detail({ presidentMemberId: 'm1' }));
    await fixture.whenStable();

    // The president's own row is not a select at all.
    expect(fixture.nativeElement.querySelector('#member-role-m1')).toBeNull();

    const select = fixture.nativeElement.querySelector('#member-role-m2') as HTMLSelectElement;
    const options = [...select.options].map((o) => o.textContent!.trim());

    expect(options).toEqual(['Secretary', 'Volunteer']);

    component.assignRole(component.detail()!, component.detail()!.members[1]!, 'rs');

    const request = http.expectOne(`${GROUP_URL}/members/m2/role`);

    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ roleId: 'rs' });
    request.flush(detail());
    reload(detail({ presidentMemberId: 'm1' }));

    expect(text()).toContain('Aarav Jain is now Secretary');
  });

  it('reads the flags, not the status, for whether nominations are open', () => {
    // A status left on NominationsOpen after the window closed is not "open".
    expect(component.stage(election({ status: 'NominationsOpen', acceptsNominations: false }))).toBe(
      'Nominations closed',
    );
    expect(component.stage(election({ status: 'NominationsOpen', acceptsNominations: true }))).toBe(
      'Nominations open',
    );
  });

  it('shows who is waiting to join, so an administrator can admit the first members', () => {
    load(detail({ iCanLead: true }), [], [application(), application({ id: 'a2', status: 'Accepted' })]);

    expect(text()).toContain('Join requests');
    expect(text()).toContain('Diya Jain');
    expect(text()).toContain('I would like to help.');
    expect(component.pending().length).toBe(1);
  });

  it('explains that a group with no members has nobody to nominate until joiners are accepted', () => {
    load({ ...detail(), members: [] });

    expect(text()).toContain('no members until somebody accepts its first joiners');
    expect(text()).toContain('Nobody is waiting');
  });

  it('accepts a joiner and re-reads the group', () => {
    load(detail(), [], [application()]);

    component.decideApplication(application(), true);

    const request = http.expectOne(`${GROUP_URL}/applications/a1/decide`);

    expect(request.request.body).toEqual({ accept: true });
    request.flush(application({ status: 'Accepted' }));
    reload();

    expect(text()).toContain('Diya Jain is now a member of Seva Sangh');
  });

  it('turns a joiner down', () => {
    load(detail(), [], [application()]);

    component.decideApplication(application(), false);

    expect(http.expectOne(`${GROUP_URL}/applications/a1/decide`).request.body).toEqual({ accept: false });
  });

  it('does not ask for the queue of a group whose president runs it', () => {
    // load() asserts no request was made when iCanLead is false.
    load(detail({ presidentMemberId: 'm1', iCanLead: false }));

    expect(text()).not.toContain('Join requests');
  });

  it('says "A member" rather than printing an id it cannot resolve', () => {
    load(detail({ presidentMemberId: 'stranger', iCanLead: false }));

    expect(text()).toContain('A member');
    expect(text()).not.toContain('stranger');
  });
});
