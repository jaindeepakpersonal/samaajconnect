import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import {
  GroupDetail,
  GroupElection,
  GroupElectionCandidate,
  GroupElectionDetail,
  LedGroup,
} from '../../core/admin.models';
import { GroupElectionsComponent } from './group-elections.component';

const GROUP_ID = 'g1';

function group(overrides: Partial<LedGroup> = {}): LedGroup {
  return {
    id: GROUP_ID,
    name: 'Seva Sangh',
    description: null,
    focusArea: null,
    presidentMemberId: null,
    status: 'Active',
    memberCount: 3,
    iAmThePresident: false,
    iCanLead: true,
    ...overrides,
  };
}

function detail(overrides: Partial<LedGroup> = {}): GroupDetail {
  return {
    group: group(overrides),
    members: [
      { memberId: 'm1', rolePosition: null, joinedAt: '2026-01-01T00:00:00Z' },
      { memberId: 'm2', rolePosition: null, joinedAt: '2026-01-01T00:00:00Z' },
    ],
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
    winnerMemberId: null,
    publishedAt: null,
    createdAt: '2026-07-01T00:00:00Z',
    ...overrides,
  };
}

function candidate(overrides: Partial<GroupElectionCandidate> = {}): GroupElectionCandidate {
  return { id: 'c1', memberId: 'm1', status: 'Nominated', nominatedBy: 'm2', votes: 0, ...overrides };
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

  function load(found: GroupDetail = detail(), elections: GroupElection[] = []) {
    fixture.detectChanges();
    http.expectOne(GROUP_URL).flush(found);
    http.expectOne(`${GROUP_URL}/elections`).flush(elections);
    http.expectOne('/v1/members?take=100').flush({ items: MEMBERS, hasMore: false });
    fixture.detectChanges();
  }

  /** What a successful action does next: re-read the group and its elections. */
  function reload(found: GroupDetail = detail(), elections: GroupElection[] = []) {
    http.expectOne(GROUP_URL).flush(found);
    http.expectOne(`${GROUP_URL}/elections`).flush(elections);
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
    expect(buttonSaying('Open the President election')).toBeDefined();
  });

  it('offers an administrator no way to open an election once the group has a president', () => {
    // The service refuses it, so the screen does not draw a control that answers 403.
    load(detail({ presidentMemberId: 'm1', iCanLead: false }));

    expect(text()).toContain('Diya Jain is this group');
    expect(buttonSaying('Open')).toBeUndefined();
  });

  it('lets the president open any position', () => {
    load(detail({ presidentMemberId: 'm1', iAmThePresident: true, iCanLead: true }));

    expect(buttonSaying('Open an election')).toBeDefined();
  });

  it('fills in and locks the position for an administrator', () => {
    load();

    component.toggleOpenForm(component.detail()!);
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('#election-position') as HTMLInputElement;

    expect(component.positionTitle).toBe('President');
    expect(input.readOnly).toBe(true);
    expect(text()).toContain('only the President election');
  });

  it('refuses a voting window that starts before nominations close', () => {
    load();

    component.positionTitle = 'President';
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

    component.positionTitle = 'President';
    component.nominationStartAt = '2026-08-01T00:00';
    component.nominationEndAt = '2026-08-20T00:00';
    component.votingStartAt = '2026-08-20T00:00';
    component.votingEndAt = '2026-09-20T00:00';

    component.open(component.detail()!);

    const request = http.expectOne((r) => r.method === 'POST' && r.url === `${GROUP_URL}/elections`);

    expect(request.request.body.positionTitle).toBe('President');
    request.flush(election());
    reload(detail(), [election()]);

    expect(text()).toContain('set up as a draft');
    expect(text()).toContain('Not started');
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

  it('gives an administrator no controls on an election that is not the President one', () => {
    load(detail(), [election({ positionTitle: 'Secretary', status: 'Draft' })]);

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

    request.flush(election({ status: 'Published', winnerMemberId: 'm1' }));
    reload(
      detail({ presidentMemberId: 'm1', iCanLead: false }),
      [election({ status: 'Published', winnerMemberId: 'm1' })],
    );

    expect(text()).toContain('winner now holds the position');
    expect(text()).toContain('Elected:');
    expect(text()).toContain('Diya Jain');
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

  it('says "A member" rather than printing an id it cannot resolve', () => {
    load(detail({ presidentMemberId: 'stranger', iCanLead: false }));

    expect(text()).toContain('A member');
    expect(text()).not.toContain('stranger');
  });
});
