import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG, CurrentUser } from '@samaajconnect/shared';
import { IssueDetailComponent } from './issue-detail.component';
import { IssuesListComponent } from './issues-list.component';
import { Issue, IssueDetail, IssueHistoryEntry, IssueStatus } from './issues.models';

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
    submittedByMemberId: ME,
    status: 'Submitted',
    isMine: true,
    availableTransitions: [],
    createdAt: new Date().toISOString(),
    publishedAt: null,
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

function providers() {
  return [
    provideRouter([]),
    provideHttpClient(),
    provideHttpClientTesting(),
    { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
  ];
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

  function load(issues: Issue[]): void {
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/v1/social-issues').flush(issues);
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  // ---- The three wireframe panels ---------------------------------------

  it('separates what the member raised from what the Samaaj published', () => {
    load([
      issue({ id: 'mine', isMine: true, status: 'Submitted' }),
      issue({ id: 'theirs', isMine: false, status: 'Published', submittedByMemberId: 'x' }),
    ]);

    expect(component.mine().map((i) => i.id)).toEqual(['mine']);
    expect(component.published().map((i) => i.id)).toEqual(['theirs']);
  });

  it('lists a member own published issue among the published ones', () => {
    // Filtering that panel by author rather than by status looked equivalent
    // and was not: a member whose issue had just gone live read "Nothing has
    // been published yet" directly underneath their own published issue.
    load([issue({ id: 'mine', isMine: true, status: 'Published' })]);

    expect(component.published().map((i) => i.id)).toEqual(['mine']);
    expect(component.mine().map((i) => i.id)).toEqual(['mine']);
    expect(text()).not.toContain('Nothing has been published yet');
  });

  it('does not count an unpublished issue as published', () => {
    load([issue({ id: 'mine', isMine: true, status: 'Submitted' })]);

    expect(component.published()).toHaveLength(0);
    expect(text()).toContain('Nothing has been published yet');
  });

  it('offers every category the service accepts, not the wireframe three', () => {
    load([]);

    // The prototype dropdown had Community, Education, Environment. The
    // validator has six.
    for (const category of ['Community', 'Education', 'Environment', 'Health', 'Safety',
      'Infrastructure']) {
      expect(text()).toContain(category);
    }
  });

  it('sends what was typed, trimmed, and keeps the new issue on screen', () => {
    load([]);

    component.title = '  Community park lighting  ';
    component.description = '  The path is dark by six.  ';
    component.category = 'Safety';
    component.locality = '  Sector 4  ';
    component.submit(true);

    const request = http.expectOne('/v1/social-issues');

    expect(request.request.body).toEqual({
      title: 'Community park lighting',
      description: 'The path is dark by six.',
      category: 'Safety',
      locality: 'Sector 4',
      submitNow: true,
    });

    request.flush(issue({ id: 'new', title: 'Community park lighting' }));
    fixture.detectChanges();

    expect(component.mine().map((i) => i.id)).toEqual(['new']);
    expect(text()).toContain('Sent for approval');
  });

  it('sends no locality rather than an empty one', () => {
    load([]);

    component.title = 'A title';
    component.description = 'A description.';
    component.locality = '   ';
    component.submit(true);

    expect(http.expectOne('/v1/social-issues').request.body).toMatchObject({ locality: null });
  });

  it('saves a draft without submitting it', () => {
    load([]);

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

  it('filters the published list by category through the service', () => {
    load([]);

    component.filter = 'Health';
    component.load();

    const request = http.expectOne((r) => r.url === '/v1/social-issues');

    expect(request.request.params.get('category')).toBe('Health');

    request.flush([]);
  });

  // ---- The progress strip -------------------------------------------------

  it('draws the wireframe strip for an issue on the happy path', () => {
    load([issue({ status: 'Approved' })]);

    const stages = component.mine()[0]!;

    expect(component.isOnHappyPath(stages)).toBe(true);
    expect(component.isReached(stages, 'Submitted')).toBe(true);
    expect(component.isReached(stages, 'Approved')).toBe(true);
    expect(component.isReached(stages, 'Published')).toBe(false);
  });

  it('draws no strip for an issue that left the path', () => {
    // Rejected, ChangesRequested and Closed are not steps towards publication.
    // A strip would say the issue is partway there when it is not going.
    for (const status of ['Rejected', 'ChangesRequested', 'Closed'] as IssueStatus[]) {
      expect(component.isOnHappyPath(issue({ status }))).toBe(false);
    }

    load([issue({ status: 'Rejected' })]);

    expect((fixture.nativeElement as HTMLElement).querySelector('.step')).toBeNull();
    expect(text()).toContain('Not accepted');
  });

  it('says what is actually happening to each of the eight states', () => {
    const statuses: IssueStatus[] = [
      'Draft', 'Submitted', 'UnderReview', 'Approved',
      'Rejected', 'ChangesRequested', 'Published', 'Closed',
    ];

    load([]);

    for (const status of statuses) {
      expect(component.describe(issue({ status })).length).toBeGreaterThan(0);
    }
  });

  it('disables Attach Evidence and says why instead of faking an upload', () => {
    load([]);

    const attach = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((b) => b.textContent?.trim().startsWith('Attach Evidence')) as HTMLButtonElement;

    expect(attach.disabled).toBe(true);
    expect(text()).toContain('no file storage');
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
      issue: issue({ status: 'UnderReview', availableTransitions: ['Approved', 'Rejected'] }),
      history: [history()],
    });

    expect(buttonSaying('Approve')).toBeDefined();
    expect(buttonSaying('Reject')).toBeDefined();

    // Not offered, because the service did not list it - even though the
    // status might suggest it.
    expect(buttonSaying('Publish')).toBeUndefined();
  });

  it('offers nothing when the service lists nothing', () => {
    load({ issue: issue({ status: 'Published', availableTransitions: [] }), history: [history()] });

    expect(text()).toContain('nothing for you to do');
    expect(buttonSaying('Approve')).toBeUndefined();
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
      issue: issue({ status: 'Approved', availableTransitions: ['Published'] }),
      history: [history()],
    });

    component.move('Published');

    expect(http.expectOne('/v1/social-issues/i1/status').request.body).toEqual({
      status: 'Published',
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

  it('offers no edit on somebody else issue', () => {
    load({
      issue: issue({ isMine: false, submittedByMemberId: 'x', status: 'Published' }),
      history: [history()],
    });

    expect(buttonSaying('Edit')).toBeUndefined();
  });

  it('keeps the form open when a correction is refused', () => {
    load({ issue: issue({ isMine: true }), history: [history()] });

    component.startEdit(component.detail()!);
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
});
