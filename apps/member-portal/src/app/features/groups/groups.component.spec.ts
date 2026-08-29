import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG, CurrentUser } from '@samaajconnect/shared';
import { GroupDetailComponent } from './group-detail.component';
import { GroupsListComponent } from './groups-list.component';
import { GroupApplication, GroupDetail, VolunteerGroup } from './groups.models';

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

function detail(overrides: Partial<VolunteerGroup> = {}, members = []): GroupDetail {
  return { group: group(overrides), members };
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

    expect(component.standing(component.groups()[0])).toBe('Not a member');
    expect(component.action(component.groups()[0])).toBe('View / Apply');
  });

  it('says an application is with the president rather than offering to apply again', () => {
    load([group({ myApplicationStatus: 'Pending' })]);

    expect(component.standing(component.groups()[0])).toBe(
      'Your application is with the president',
    );
    expect(component.action(component.groups()[0])).toBe('View');
  });

  it('reports a rejected application honestly', () => {
    load([group({ myApplicationStatus: 'Rejected' })]);

    expect(component.standing(component.groups()[0])).toBe('Your application was not accepted');
  });

  it('tells a president they lead the group, not that they are a member', () => {
    // A president is also a member, so ordering matters: without it every
    // president would read "You are a member".
    load([group({ iAmThePresident: true, iAmAMember: true })]);

    expect(component.standing(component.groups()[0])).toBe('You lead this group');
  });

  it('does not offer to apply to a group that takes no new members', () => {
    load([group({ status: 'Inactive' })]);

    expect(component.action(component.groups()[0])).toBe('View');
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
    expect(component.action(component.groups()[0])).toBe('Manage');
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

  /** Answers /me and the group; the queue only when the reader leads it. */
  function load(found: GroupDetail, applications: GroupApplication[] | null = null): void {
    fixture.detectChanges();

    http.expectOne('/v1/identity/me').flush(member);
    http.expectOne('/v1/volunteer-groups/groups/g1').flush(found);

    if (applications !== null) {
      http.expectOne('/v1/volunteer-groups/groups/g1/applications').flush(applications);
    }

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

  it('sends the position the president typed alongside the acceptance', () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), [application()]);

    component.roleDrafts['a1'] = ' Secretary ';
    component.decide(component.pending()[0], true);

    const request = http.expectOne(
      '/v1/volunteer-groups/groups/g1/applications/a1/decide',
    );

    expect(request.request.body).toEqual({ accept: true, rolePosition: 'Secretary' });

    request.flush(application({ status: 'Accepted' }));

    // Accepting adds a member, so the group and the queue are both re-read.
    http
      .expectOne('/v1/volunteer-groups/groups/g1')
      .flush(detail({ iAmThePresident: true, iAmAMember: true, memberCount: 83 }));
    http.expectOne('/v1/volunteer-groups/groups/g1/applications').flush([]);
    fixture.detectChanges();

    expect(text()).toContain('Nobody is waiting');
  });

  it('sends no position when the president left the box empty', () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), [application()]);

    component.decide(component.pending()[0], false);

    expect(
      http.expectOne('/v1/volunteer-groups/groups/g1/applications/a1/decide').request.body,
    ).toEqual({ accept: false, rolePosition: null });
  });

  it('keeps a refused decision beside its own application', () => {
    load(detail({ iAmThePresident: true, iAmAMember: true }), [
      application({ id: 'a1' }),
      application({ id: 'a2' }),
    ]);

    component.decide(component.pending()[0], true);

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

  // ---- Names it cannot resolve ---------------------------------------------

  it('names the reader but nobody else', () => {
    const found = detail({ iAmThePresident: true, iAmAMember: true });

    load(found, []);

    expect(component.nameFor(ME, found)).toBe('You');
    expect(component.nameFor(PRESIDENT, found)).toBe('The president');
    expect(component.nameFor('somebody-else', found)).toBe('A member');
  });

  it('says there is no notification channel rather than implying one', () => {
    load(detail({ myApplicationStatus: 'Pending' }));

    expect(text()).toContain('check back here');
  });
});
