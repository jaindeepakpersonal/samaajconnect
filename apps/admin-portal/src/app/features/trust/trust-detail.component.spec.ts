import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { Trust, TrustElection, TrustMember, TrustRole } from '../../core/admin.models';
import { LocalityOptions } from '../../core/locality-options';
import { TrustDetailComponent } from './trust-detail.component';
import { TrustElectionsComponent } from './trust-elections.component';
import { TrustMemberDialogComponent } from './trust-member-dialog.component';
import { TrustMembersComponent } from './trust-members.component';
import { TrustRolesComponent } from './trust-roles.component';

const TRUST_ID = 't1';

function trust(overrides: Partial<Trust> = {}): Trust {
  return {
    id: TRUST_ID,
    localityId: 'loc1',
    name: 'Hiran Magri Samaaj Trust',
    status: 'Active',
    createdAt: '2026-09-01T00:00:00Z',
    ...overrides,
  };
}

const TRUSTEE: TrustRole = { id: 'r0', title: 'Trustee', kind: 'Trustee', seats: null, holders: 1, isElected: false };
const TREASURER: TrustRole = { id: 'r1', title: 'Treasurer', kind: 'Elected', seats: 1, holders: 0, isElected: true };
const SECRETARY: TrustRole = { id: 'r2', title: 'Secretary', kind: 'Elected', seats: 2, holders: 2, isElected: true };

function member(overrides: Partial<TrustMember> = {}): TrustMember {
  return {
    id: 'tm1',
    trustId: TRUST_ID,
    memberId: 'm1',
    positionTitle: 'Trustee',
    roleId: TRUSTEE.id,
    status: 'Active',
    addedBy: 'admin1',
    addedAt: '2026-09-02T00:00:00Z',
    removedBy: null,
    removedAt: null,
    ...overrides,
  };
}

function election(overrides: Partial<TrustElection> = {}): TrustElection {
  return {
    id: 'e1',
    trustId: TRUST_ID,
    positionTitle: 'Secretary',
    nominationStartAt: '2026-10-01T00:00:00Z',
    nominationEndAt: '2026-10-10T00:00:00Z',
    votingStartAt: '2026-10-10T00:00:00Z',
    votingEndAt: '2026-10-20T00:00:00Z',
    status: 'Published',
    acceptsNominations: false,
    acceptsVotes: false,
    myVoteCandidateIds: [],
    candidateCount: 3,
    winnerMemberIds: ['m1', 'm2'],
    publishedAt: '2026-10-21T00:00:00Z',
    createdAt: '2026-09-20T00:00:00Z',
    roleId: SECRETARY.id,
    seats: 2,
    ...overrides,
  };
}

const MEMBERS = [
  { id: 'm1', fullName: 'Diya Jain' },
  { id: 'm2', fullName: 'Ravi Mehta' },
];

const LOCALITIES = {
  provide: LocalityOptions,
  useValue: {
    load: () => undefined,
    options: () => [{ id: 'loc1', label: 'Hiran Magri, Udaipur' }],
    label: (id: string | null) => (id === 'loc1' ? 'Hiran Magri, Udaipur' : 'Whole Samaaj'),
  },
};

function configure(component: unknown, routed = false): void {
  TestBed.configureTestingModule({
    imports: [component as never],
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      LOCALITIES,
      ...(routed
        ? [{ provide: ActivatedRoute, useValue: { snapshot: { paramMap: new Map([['id', TRUST_ID]]) } } }]
        : []),
    ],
  });
}

describe('TrustDetailComponent', () => {
  let fixture: ComponentFixture<TrustDetailComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    configure(TrustDetailComponent, true);
    fixture = TestBed.createComponent(TrustDetailComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load() {
    fixture.detectChanges();
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}`).flush(trust());
    fixture.detectChanges();
    http.expectOne('/v1/members?take=100').flush({ items: MEMBERS, hasMore: false });
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/members`).flush([member()]);
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/roles`).flush([TRUSTEE]);
    fixture.detectChanges();
  }

  it('has the six tabs, opens on Members, and loads another tab only when it is opened', () => {
    load();

    const tabs = [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('[role="tab"]')];

    expect(tabs.map((t) => t.textContent?.trim())).toEqual([
      'Members',
      'Roles',
      'Elections',
      'Donations',
      'Collections',
      'Expenses',
    ]);
    expect(text()).toContain('Diya Jain');
    expect(text()).toContain('Hiran Magri, Udaipur');

    http.expectNone(`/v1/trust/trusts/${TRUST_ID}/collections`);

    tabs[4]!.click();
    fixture.detectChanges();

    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/collections`).flush([]);
    fixture.detectChanges();

    expect(text()).toContain('Nothing is owed to this Trust yet');
    expect(text()).not.toContain('Add a trustee');
  });

  it('reads a 404 as no such Trust', () => {
    fixture.detectChanges();
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}`).flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('No such Trust');
  });
});

describe('TrustMembersComponent', () => {
  let fixture: ComponentFixture<TrustMembersComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    configure(TrustMembersComponent);
    fixture = TestBed.createComponent(TrustMembersComponent);
    fixture.componentRef.setInput('trustId', TRUST_ID);
    fixture.componentRef.setInput('names', new Map(MEMBERS.map((m) => [m.id, m.fullName])));
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(members: TrustMember[], roles: TrustRole[] = [TRUSTEE, TREASURER, SECRETARY]) {
    fixture.detectChanges();
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/members`).flush(members);
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/roles`).flush(roles);
    fixture.detectChanges();
  }

  it('says when nobody has been added yet', () => {
    load([]);

    expect(text()).toContain('Nobody added yet');
  });

  it('offers a trustee only the roles with a free seat, and their own', () => {
    load([member()]);

    const options = [...(fixture.nativeElement as HTMLElement).querySelectorAll('select option')].map((o) =>
      o.textContent?.trim(),
    );

    expect(options).toEqual(['Trustee', 'Treasurer']);
    expect(text()).toContain('1 trustee');
  });

  it('moves a trustee into a role, and re-reads the roster', () => {
    load([member()]);

    fixture.componentInstance.assign(member(), TREASURER.id);

    const request = http.expectOne(`/v1/trust/trusts/${TRUST_ID}/members/tm1/role`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ roleId: TREASURER.id });
    request.flush(member({ roleId: TREASURER.id, positionTitle: 'Treasurer' }));

    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/members`).flush([member({ roleId: TREASURER.id })]);
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/roles`).flush([TRUSTEE, TREASURER]);
    fixture.detectChanges();

    expect(text()).toContain('Diya Jain is now Treasurer');
  });

  it('removes a member with no confirmation step', () => {
    load([member()]);

    fixture.componentInstance.remove(member());

    http
      .expectOne(`/v1/trust/trusts/${TRUST_ID}/members/tm1`)
      .flush(member({ status: 'Removed', removedBy: 'admin1', removedAt: '2026-09-05T00:00:00Z' }));
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/members`).flush([member({ status: 'Removed' })]);
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/roles`).flush([TRUSTEE]);
    fixture.detectChanges();

    expect(text()).toContain('off the roster');
  });

  it('adds a trustee through the popup', () => {
    load([]);

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.section-head .btn')!
      .click();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[role="dialog"]')).not.toBeNull();

    fixture.componentInstance.added('Diya Jain is on the roster as Trustee.');
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/members`).flush([member()]);
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/roles`).flush([TRUSTEE]);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[role="dialog"]')).toBeNull();
    expect(text()).toContain('on the roster as Trustee');
  });
});

describe('TrustMemberDialogComponent', () => {
  let fixture: ComponentFixture<TrustMemberDialogComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    configure(TrustMemberDialogComponent);
    fixture = TestBed.createComponent(TrustMemberDialogComponent);
    fixture.componentRef.setInput('trustId', TRUST_ID);
    fixture.componentRef.setInput('roles', [TRUSTEE, TREASURER, SECRETARY]);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('searches, picks, and adds with the role chosen - never a full one', () => {
    const component = fixture.componentInstance;
    let saved = '';
    component.saved.subscribe((message) => (saved = message));

    component.term = 'Diya';
    component.search();
    http.expectOne('/v1/members?skip=0&take=20&term=Diya').flush({ items: [MEMBERS[0]], hasMore: false });

    component.select(MEMBERS[0] as never);
    fixture.detectChanges();

    expect(component.openRoles().map((r) => r.title)).toEqual(['Trustee', 'Treasurer']);
    expect(component.roleId).toBe(TRUSTEE.id);

    component.roleId = TREASURER.id;
    component.add(MEMBERS[0] as never);

    const request = http.expectOne(`/v1/trust/trusts/${TRUST_ID}/members`);
    expect(request.request.body).toEqual({ memberId: 'm1', roleId: TREASURER.id });
    request.flush(member({ roleId: TREASURER.id }));

    expect(saved).toBe('Diya Jain is on the roster as Treasurer.');
  });

  it('sends no role for Trustee, the default', () => {
    const component = fixture.componentInstance;

    component.select(MEMBERS[0] as never);
    component.add(MEMBERS[0] as never);

    expect(http.expectOne(`/v1/trust/trusts/${TRUST_ID}/members`).request.body).toEqual({
      memberId: 'm1',
      roleId: null,
    });
  });
});

describe('TrustRolesComponent', () => {
  let fixture: ComponentFixture<TrustRolesComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    configure(TrustRolesComponent);
    fixture = TestBed.createComponent(TrustRolesComponent);
    fixture.componentRef.setInput('trustId', TRUST_ID);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/roles`).flush([TRUSTEE, TREASURER, SECRETARY]);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  it('lists the roles, offering Trustee nothing and not removing one somebody holds', () => {
    expect(text()).toContain('No limit');

    const rows = [...(fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr')];
    expect(rows[0]!.querySelectorAll('button').length).toBe(0);

    const secretaryRemove = [...rows[2]!.querySelectorAll('button')].find((b) =>
      b.textContent?.includes('Remove'),
    );
    expect(secretaryRemove?.disabled).toBe(true);
  });

  it('defines a role with its seats', () => {
    const component = fixture.componentInstance;
    component.newTitle = ' President ';
    component.newSeats = 1;
    component.define();

    const request = http.expectOne(`/v1/trust/trusts/${TRUST_ID}/roles`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ title: 'President', seats: 1 });
    request.flush({ ...TREASURER, id: 'r3', title: 'President' });

    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/roles`).flush([TRUSTEE]);
    fixture.detectChanges();

    expect(text()).toContain('President is a role on this Trust');
  });

  it('will not resize a role below its holders', () => {
    const component = fixture.componentInstance;
    component.startEdit(SECRETARY);
    component.editSeats = 1;
    fixture.detectChanges();

    expect(text()).toContain('2 hold it now');
    expect(component.valid('Secretary', 0)).toBe(false);
    expect(component.valid('Secretary', 51)).toBe(false);
  });
});

describe('TrustElectionsComponent', () => {
  let fixture: ComponentFixture<TrustElectionsComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    configure(TrustElectionsComponent);
    fixture = TestBed.createComponent(TrustElectionsComponent);
    fixture.componentRef.setInput('trustId', TRUST_ID);
    fixture.componentRef.setInput('names', new Map(MEMBERS.map((m) => [m.id, m.fullName])));
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(elections: TrustElection[]) {
    fixture.detectChanges();
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/elections`).flush(elections);
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/roles`).flush([TRUSTEE, TREASURER, SECRETARY]);
    fixture.detectChanges();
  }

  it('names every winner of a published election, with its seats', () => {
    load([election()]);

    expect(text()).toContain('Secretary');
    expect(text()).toContain('2 seats');
    expect(text()).toContain('Diya Jain');
    expect(text()).toContain('Ravi Mehta');
  });

  it('offers only elected roles with no election under way', () => {
    load([election({ status: 'VotingOpen', roleId: TREASURER.id, winnerMemberIds: [] })]);

    expect(fixture.componentInstance.electable().map((r) => r.title)).toEqual(['Secretary']);
  });

  it('only sets an election up for a chosen role, once the windows are valid and forward-ordered', () => {
    load([]);

    const component = fixture.componentInstance;
    component.nominationStartAt = '2026-10-01T00:00';
    component.nominationEndAt = '2026-10-10T00:00';
    component.votingStartAt = '2026-10-11T00:00';
    component.votingEndAt = '2026-10-20T00:00';

    expect(component.canOpen()).toBe(false);

    component.roleId = TREASURER.id;
    expect(component.canOpen()).toBe(true);

    // Voting starts before nominations close.
    component.votingStartAt = '2026-10-05T00:00';
    expect(component.canOpen()).toBe(false);

    component.votingStartAt = '2026-10-11T00:00';
    component.open();

    const request = http.expectOne(`/v1/trust/trusts/${TRUST_ID}/elections`);
    expect(request.request.body.roleId).toBe(TREASURER.id);
    request.flush(election({ status: 'Draft', roleId: TREASURER.id }));

    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/elections`).flush([]);
  });
});
