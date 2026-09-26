import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG, CurrentUser } from '@samaajconnect/shared';
import { VolunteerGroup } from '../groups/groups.models';
import { IssueDetailComponent } from './issue-detail.component';
import { IssuesListComponent } from './issues-list.component';
import { Issue, IssueDetail, IssueHistoryEntry, IssueStatus, ReviewAccess } from './issues.models';

const ME = 'u1';
const REVIEWER = 'r1';

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
  permissions: ['Members.Read'],
};

function issue(overrides: Partial<Issue> = {}): Issue {
  return {
    id: 'i1',
    title: 'Road safety near the community school',
    description: 'Cars come through too fast at closing time.',
    category: 'Safety',
    locality: 'Hiran Magri',
    localityId: null,
    submittedByMemberId: ME,
    status: 'Submitted',
    isMine: true,
    availableTransitions: [],
    canDecide: false,
    createdAt: new Date().toISOString(),
    decision: null,
    monitoringGroupId: null,
    decidedAt: null,
    ...overrides,
  };
}

function history(overrides: Partial<IssueHistoryEntry> = {}): IssueHistoryEntry {
  return {
    fromStatus: null,
    toStatus: 'Submitted',
    actorUserId: ME,
    reason: null,
    createdAt: new Date().toISOString(),
    ...overrides,
  };
}

function access(overrides: Partial<ReviewAccess> = {}): ReviewAccess {
  return { canReview: false, canSeeQueue: false, panelSeated: false, isPanelMember: false, ...overrides };
}

function group(overrides: Partial<VolunteerGroup> = {}): VolunteerGroup {
  return {
    id: 'g1',
    name: 'Road Safety Watch',
    description: null,
    focusArea: null,
    presidentMemberId: null,
    status: 'Active',
    memberCount: 5,
    pendingApplicationCount: 0,
    myApplicationStatus: null,
    iAmAMember: false,
    iAmThePresident: false,
    iCanLead: false,
    iCanRunElections: false,
    createdAt: '2026-01-01T00:00:00Z',
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

/** Answers the Locality select's two master-data reads. */
function flushLocalities(http: HttpTestingController): void {
  http
    .expectOne((r) => r.url === '/v1/master-data/localities')
    .flush([{ id: 'loc-hm', cityId: 'city-u', name: 'Hiran Magri', isActive: true }]);
  http
    .expectOne((r) => r.url === '/v1/master-data/cities')
    .flush([{ id: 'city-u', countryId: 'in', name: 'Udaipur', isActive: true }]);
}

describe('IssuesListComponent', () => {
  let fixture: ComponentFixture<IssuesListComponent>;
  let component: IssuesListComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [IssuesListComponent], providers: providers() });

    fixture = TestBed.createComponent(IssuesListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function load(issues: Issue[], grant: ReviewAccess = access()): void {
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/v1/social-issues').flush(issues);
    http.expectOne('/v1/social-issues/review-access').flush(grant);
    flushLocalities(http);
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  // ---- The tabs -----------------------------------------------------------

  it('separates what the member raised from what the Samaaj can see', () => {
    load([
      issue({ id: 'mine', isMine: true, status: 'Submitted' }),
      issue({
        id: 'theirs',
        isMine: false,
        status: 'Closed',
        decidedAt: '2026-09-01T00:00:00Z',
        submittedByMemberId: 'x',
      }),
    ]);

    expect(component.mine().map((i) => i.id)).toEqual(['mine']);
    expect(component.visible().map((i) => i.id)).toEqual(['theirs']);
  });

  it('lists a member own decided issue among the visible ones', () => {
    // Filtering that panel by author rather than by status looked equivalent
    // and was not: a member whose issue had just been decided read "Nothing
    // is... yet" directly underneath their own decided issue.
    load([issue({ id: 'mine', isMine: true, status: 'Closed', decidedAt: '2026-09-01T00:00:00Z' })]);

    expect(component.visible().map((i) => i.id)).toEqual(['mine']);
    expect(component.mine().map((i) => i.id)).toEqual(['mine']);
    expect(text()).not.toContain('Nothing is in front of the Panch');
  });

  it('does not count an undecided issue as visible to the Samaaj', () => {
    load([issue({ id: 'mine', isMine: true, status: 'Submitted' })]);

    expect(component.visible()).toHaveLength(0);
    expect(text()).toContain('Nothing is in front of the Panch');
  });

  it('does not count a withdrawn issue (Closed with no decision) as visible', () => {
    load([issue({ id: 'mine', isMine: true, status: 'Closed', decidedAt: null })]);

    expect(component.visible()).toHaveLength(0);
  });

  it('offers every category the service accepts, not the wireframe three', () => {
    load([]);

    for (const category of ['Community', 'Education', 'Environment', 'Health', 'Safety',
      'Infrastructure']) {
      expect(text()).toContain(category);
    }
  });

  it('sends what was typed, trimmed, and keeps the new issue on screen', () => {
    load([]);
    component.activeTab.set('raise');
    fixture.detectChanges();

    component.title = '  Community park lighting  ';
    component.description = '  The path is dark by six.  ';
    component.category = 'Safety';
    component.localityId = 'loc-hm';
    component.submit(true);

    const request = http.expectOne('/v1/social-issues');

    // The id is what an Area Admin's review is scoped by; the label is what
    // every screen shows.
    expect(request.request.body).toEqual({
      title: 'Community park lighting',
      description: 'The path is dark by six.',
      category: 'Safety',
      locality: 'Hiran Magri, Udaipur',
      localityId: 'loc-hm',
      submitNow: true,
    });

    request.flush(issue({ id: 'new', title: 'Community park lighting' }));
    fixture.detectChanges();

    expect(component.mine().map((i) => i.id)).toEqual(['new']);
    expect(text()).toContain('Sent for review');
  });

  it('sends no locality rather than an empty one', () => {
    load([]);

    component.title = 'A title';
    component.description = 'A description.';
    component.localityId = '';
    component.submit(true);

    expect(http.expectOne('/v1/social-issues').request.body).toMatchObject({
      locality: null,
      localityId: null,
    });
  });

  it('saves a draft without submitting it', () => {
    load([]);
    component.activeTab.set('raise');
    fixture.detectChanges();

    component.title = 'Half-written';
    component.description = 'Coming back to this.';
    component.submit(false);

    const request = http.expectOne('/v1/social-issues');

    expect(request.request.body).toMatchObject({ submitNow: false });

    request.flush(issue({ id: 'draft', status: 'Draft' }));
    fixture.detectChanges();

    expect(text()).toContain('Only you can see it');
  });

  it('keeps what was typed when submitting fails', () => {
    load([]);

    component.title = 'A title';
    component.description = 'A description.';
    component.submit(true);

    http
      .expectOne('/v1/social-issues')
      .flush({ title: 'Validation', detail: 'No.' }, { status: 400, statusText: 'Bad Request' });

    fixture.detectChanges();

    expect(component.title).toBe('A title');
    expect(component.formError()).not.toBeNull();
  });

  it('filters the visible list by category through the service', () => {
    load([]);

    component.filter = 'Health';
    component.load();

    const request = http.expectOne((r) => r.url === '/v1/social-issues');

    expect(request.request.params.get('category')).toBe('Health');

    request.flush([]);
  });

  // ---- The progress strip -------------------------------------------------

  it('draws the strip for an issue on the happy path', () => {
    load([issue({ status: 'DecisionPending' })]);

    const stages = component.mine()[0]!;

    expect(component.isOnHappyPath(stages)).toBe(true);
    expect(component.isReached(stages, 'Submitted')).toBe(true);
    expect(component.isReached(stages, 'DecisionPending')).toBe(true);
    expect(component.isReached(stages, 'Closed')).toBe(false);
  });

  it('draws no strip for an issue that left the path', () => {
    // Rejected and ChangesRequested are not steps towards a decision. A strip
    // would say the issue is partway there when it is not going.
    for (const status of ['Rejected', 'ChangesRequested'] as IssueStatus[]) {
      expect(component.isOnHappyPath(issue({ status }))).toBe(false);
    }

    load([issue({ status: 'Rejected' })]);
    component.activeTab.set('mine');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.step')).toBeNull();
    expect(text()).toContain('Not accepted');
  });

  it('draws no strip for a withdrawn issue either', () => {
    // Closed with no decision is off the path the same as Rejected is.
    expect(component.isOnHappyPath(issue({ status: 'Closed', decidedAt: null }))).toBe(false);
    expect(
      component.isOnHappyPath(issue({ status: 'Closed', decidedAt: '2026-09-01T00:00:00Z' })),
    ).toBe(true);
  });

  it('says what is actually happening to each of the seven states', () => {
    const statuses: IssueStatus[] = [
      'Draft', 'Submitted', 'UnderReview', 'DecisionPending',
      'Rejected', 'ChangesRequested', 'Closed',
    ];

    load([]);

    for (const status of statuses) {
      expect(component.describe(issue({ status })).length).toBeGreaterThan(0);
    }
  });

  it('disables Attach Evidence and says why instead of faking an upload', () => {
    load([]);
    component.activeTab.set('raise');
    fixture.detectChanges();

    const attach = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((b) => b.textContent?.trim().startsWith('Attach Evidence')) as HTMLButtonElement;

    expect(attach.disabled).toBe(true);
    expect(text()).toContain('no file storage');
  });

  // ---- Tabs -----------------------------------------------------------------

  it('shows Samaaj issues first', () => {
    load([]);

    expect(component.activeTab()).toBe('community');
    expect(text()).toContain('Nothing is in front of the Panch');
  });

  it('switches to Raise an issue and back without losing the community list', () => {
    load([]);

    component.activeTab.set('raise');
    fixture.detectChanges();

    expect(text()).toContain('Submit for review');
    expect(text()).not.toContain('Nothing is in front of the Panch');

    component.activeTab.set('community');
    fixture.detectChanges();

    expect(text()).toContain('Nothing is in front of the Panch');
  });

  // ---- The Panch's tab ------------------------------------------------------

  it('offers no "For the Panch" tab to somebody who cannot see the queue', () => {
    load([], access({ canSeeQueue: false }));

    expect(text()).not.toContain('For the Panch');
  });

  it('offers the tab, and loads the queue only once it is opened', () => {
    load([], access({ canSeeQueue: true, canReview: true, isPanelMember: true, panelSeated: true }));

    expect(text()).toContain('For the Panch');
    http.expectNone('/v1/social-issues/approval-queue');

    component.openPanchTab();

    const request = http.expectOne('/v1/social-issues/approval-queue');

    request.flush([issue({ id: 'waiting', status: 'DecisionPending', canDecide: true })]);
    fixture.detectChanges();

    expect(text()).toContain('Ready for a decision');

    // Reopening does not ask again.
    component.activeTab.set('community');
    fixture.detectChanges();
    component.openPanchTab();
    http.expectNone('/v1/social-issues/approval-queue');
  });
});

describe('IssueDetailComponent', () => {
  let fixture: ComponentFixture<IssueDetailComponent>;
  let component: IssueDetailComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [IssueDetailComponent],
      providers: [
        ...providers(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'i1' } } } },
      ],
    });

    fixture = TestBed.createComponent(IssueDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function load(detail: IssueDetail): void {
    fixture.detectChanges();
    http.expectOne('/v1/identity/me').flush(member);
    http.expectOne('/v1/social-issues/i1').flush(detail);

    if (detail.issue.canDecide || detail.issue.monitoringGroupId !== null) {
      http.expectOne('/v1/volunteer-groups/groups').flush([group()]);
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

  // ---- Buttons come from the server, never from the status ---------------

  it('renders exactly the transitions the service said are legal', () => {
    load({
      issue: issue({ status: 'UnderReview', availableTransitions: ['DecisionPending', 'Rejected'] }),
      history: [history()],
    });

    expect(buttonSaying('Put in front of the Panch')).toBeDefined();
    expect(buttonSaying('Reject')).toBeDefined();
  });

  it('offers nothing when the service lists nothing and there is no decision to make', () => {
    load({
      issue: issue({ status: 'Closed', availableTransitions: [], decidedAt: '2026-09-01T00:00:00Z' }),
      history: [history()],
    });

    expect(text()).toContain('nothing for you to do');
    expect(buttonSaying('Reject')).toBeUndefined();
  });

  it('serves a reviewer and an author from the same list without knowing which', () => {
    load({
      issue: issue({ isMine: false, status: 'Submitted', availableTransitions: ['UnderReview'] }),
      history: [history()],
    });

    expect(buttonSaying('Start reviewing')).toBeDefined();
  });

  // ---- Reasons -------------------------------------------------------------

  it('asks for a reason before sending a move that needs one', () => {
    load({
      issue: issue({ status: 'UnderReview', availableTransitions: ['Rejected'] }),
      history: [history()],
    });

    component.move('Rejected');
    fixture.detectChanges();

    // Nothing sent yet - http.verify() in afterEach is the assertion.
    expect(component.pendingMove()).toBe('Rejected');
    expect(text()).toContain('needs one');
  });

  it('will not send that move with an empty reason', () => {
    load({
      issue: issue({ status: 'UnderReview', availableTransitions: ['ChangesRequested'] }),
      history: [history()],
    });

    component.move('ChangesRequested');
    component.reason = '   ';
    component.confirmMove();

    // Still nothing sent.
    expect(component.pendingMove()).toBe('ChangesRequested');
  });

  it('sends the reason once it has one, then re-reads the issue', () => {
    load({
      issue: issue({ status: 'UnderReview', availableTransitions: ['Rejected'] }),
      history: [history()],
    });

    component.move('Rejected');
    component.reason = '  Please add the road name.  ';
    component.confirmMove();

    const request = http.expectOne('/v1/social-issues/i1/status');

    expect(request.request.body).toEqual({
      status: 'Rejected',
      reason: 'Please add the road name.',
    });

    request.flush(issue({ status: 'Rejected' }));

    // The move added a history entry and changed what is legal next.
    http.expectOne('/v1/social-issues/i1').flush({
      issue: issue({ status: 'Rejected', availableTransitions: [] }),
      history: [history(), history({ toStatus: 'Rejected', reason: 'Please add the road name.' })],
    });

    fixture.detectChanges();

    expect(component.pendingMove()).toBeNull();
  });

  it('sends a move that needs no reason straight away', () => {
    load({
      issue: issue({ status: 'UnderReview', availableTransitions: ['DecisionPending'] }),
      history: [history()],
    });

    component.move('DecisionPending');

    expect(http.expectOne('/v1/social-issues/i1/status').request.body).toEqual({
      status: 'DecisionPending',
      reason: null,
    });
  });

  // ---- The question the history exists to answer --------------------------

  it('puts the reason where an author will actually see it', () => {
    // "Why was mine sent back?" should not require reading a timeline.
    load({
      issue: issue({ status: 'ChangesRequested', availableTransitions: ['Submitted'] }),
      history: [
        history(),
        history({
          fromStatus: 'UnderReview',
          toStatus: 'ChangesRequested',
          actorUserId: REVIEWER,
          reason: 'Please add the road name.',
        }),
      ],
    });

    expect(component.latestReason()).toBe('Please add the road name.');
    expect(text()).toContain('What was asked for:');
    expect(text()).toContain('Please add the road name.');
  });

  it('takes the most recent reason when there have been several', () => {
    load({
      issue: issue({ status: 'Rejected', availableTransitions: [] }),
      history: [
        history({ toStatus: 'ChangesRequested', reason: 'First note.' }),
        history({ toStatus: 'Rejected', reason: 'Second note.' }),
      ],
    });

    expect(component.latestReason()).toBe('Second note.');
    expect(text()).toContain('Why this was not accepted:');
  });

  it('says nothing about a reason when no step carried one', () => {
    load({ issue: issue({ status: 'Submitted' }), history: [history()] });

    expect(component.latestReason()).toBeNull();
  });

  it('shows the whole history, naming the reader but nobody else', () => {
    load({
      issue: issue({ status: 'Rejected', availableTransitions: [] }),
      history: [
        history({ actorUserId: ME }),
        history({ fromStatus: 'Submitted', toStatus: 'UnderReview', actorUserId: REVIEWER }),
      ],
    });

    expect(component.actor(ME)).toBe('You');
    expect(component.actor(REVIEWER)).toBe('A reviewer');
    expect(text()).not.toContain(REVIEWER);
  });

  // ---- Correcting it -------------------------------------------------------

  it('lets the author correct their own issue', () => {
    load({
      issue: issue({ isMine: true, status: 'ChangesRequested', availableTransitions: ['Submitted'] }),
      history: [history()],
    });

    expect(buttonSaying('Edit')).toBeDefined();

    component.startEdit(component.detail()!);
    flushLocalities(http);
    component.title = 'Road safety near the school gate';
    component.saveEdit();

    const request = http.expectOne('/v1/social-issues/i1');

    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toMatchObject({ title: 'Road safety near the school gate' });

    request.flush(issue());
    http.expectOne('/v1/social-issues/i1').flush({ issue: issue(), history: [history()] });
    fixture.detectChanges();

    expect(component.editing()).toBe(false);
  });

  it('keeps a locality typed before localities were picked, unless another is chosen', () => {
    // The service replaces both fields on a revision, so leaving the select
    // alone must send the typed text back rather than clear it.
    load({ issue: issue({ isMine: true, locality: 'Sector 4', localityId: null }), history: [history()] });

    component.startEdit(component.detail()!);
    flushLocalities(http);
    component.saveEdit();

    expect(http.expectOne('/v1/social-issues/i1').request.body).toMatchObject({
      locality: 'Sector 4',
      localityId: null,
    });
  });

  it('sends a picked locality by id, which is what an Area Admin reviews by', () => {
    load({ issue: issue({ isMine: true, locality: 'Sector 4', localityId: null }), history: [history()] });

    component.startEdit(component.detail()!);
    flushLocalities(http);
    component.localityId = 'loc-hm';
    component.saveEdit();

    expect(http.expectOne('/v1/social-issues/i1').request.body).toMatchObject({
      locality: 'Hiran Magri, Udaipur',
      localityId: 'loc-hm',
    });
  });

  it('offers no edit on somebody else issue', () => {
    load({
      issue: issue({ isMine: false, submittedByMemberId: 'x', status: 'DecisionPending' }),
      history: [history()],
    });

    expect(buttonSaying('Edit')).toBeUndefined();
  });

  it('keeps the form open when a correction is refused', () => {
    load({ issue: issue({ isMine: true }), history: [history()] });

    component.startEdit(component.detail()!);
    flushLocalities(http);
    component.title = 'Something';
    component.saveEdit();

    http
      .expectOne('/v1/social-issues/i1')
      .flush({ title: 'Issue.Decided', detail: 'No.' }, { status: 409, statusText: 'Conflict' });

    fixture.detectChanges();

    // A refusal must not cost what was typed.
    expect(component.editing()).toBe(true);
    expect(component.actionError()).not.toBeNull();
  });

  it('offers a retry when the issue cannot be loaded', () => {
    fixture.detectChanges();

    http.expectOne('/v1/identity/me').flush(member);
    http.expectOne('/v1/social-issues/i1').flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('Try again');
  });

  // ---- The Panch's decision -------------------------------------------------

  it('offers no decision form to somebody who may not decide', () => {
    load({
      issue: issue({ status: 'DecisionPending', canDecide: false, availableTransitions: [] }),
      history: [history()],
    });

    expect(text()).not.toContain("Record the Panch's decision");
  });

  it('offers the decision form to a Panch member, with the volunteer groups to choose from', () => {
    load({
      issue: issue({ status: 'DecisionPending', canDecide: true, availableTransitions: [] }),
      history: [history()],
    });

    expect(text()).toContain("Record the Panch's decision");
    expect(text()).toContain('Road Safety Watch');
  });

  it('will not record an empty decision', () => {
    load({
      issue: issue({ status: 'DecisionPending', canDecide: true, availableTransitions: [] }),
      history: [history()],
    });

    component.decisionText = '   ';
    component.decide();

    http.expectNone('/v1/social-issues/i1/decision');
  });

  it('records a decision and hands monitoring to the chosen group', () => {
    load({
      issue: issue({ status: 'DecisionPending', canDecide: true, availableTransitions: [] }),
      history: [history()],
    });

    component.decisionText = '  Repaint the crossing.  ';
    component.monitoringGroupId = 'g1';
    component.decide();

    const request = http.expectOne('/v1/social-issues/i1/decision');

    expect(request.request.body).toEqual({
      decision: 'Repaint the crossing.',
      monitoringGroupId: 'g1',
    });

    request.flush({ issue: issue({ status: 'Closed' }), history: [history()] });

    http.expectOne('/v1/social-issues/i1').flush({
      issue: issue({
        status: 'Closed',
        canDecide: false,
        decision: 'Repaint the crossing.',
        monitoringGroupId: 'g1',
        decidedAt: '2026-09-01T00:00:00Z',
      }),
      history: [history()],
    });
    http.expectOne('/v1/volunteer-groups/groups').flush([group()]);
    fixture.detectChanges();

    expect(text()).toContain("The Panch's decision");
    expect(text()).toContain('Road Safety Watch');
    expect(component.decisionText).toBe('');
  });

  it('sends no monitoring group when none was chosen', () => {
    load({
      issue: issue({ status: 'DecisionPending', canDecide: true, availableTransitions: [] }),
      history: [history()],
    });

    component.decisionText = 'Nothing further is needed.';
    component.decide();

    expect(http.expectOne('/v1/social-issues/i1/decision').request.body).toMatchObject({
      monitoringGroupId: null,
    });
  });

  it('shows a recorded decision read-only, resolving the group name', () => {
    load({
      issue: issue({
        status: 'Closed',
        availableTransitions: [],
        decision: 'Repaint the crossing.',
        monitoringGroupId: 'g1',
        decidedAt: '2026-09-01T00:00:00Z',
      }),
      history: [history()],
    });

    expect(text()).toContain('Repaint the crossing.');
    expect(text()).toContain('Road Safety Watch');
    expect(text()).not.toContain("Record the Panch's decision");
  });
});
