import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { IssueQueueComponent } from './issue-queue.component';
import { IssueReviewAccess, SocialIssue } from '../../core/admin.models';

function issue(overrides: Partial<SocialIssue> = {}): SocialIssue {
  return {
    id: 'i1',
    title: 'Streetlights out on the temple road',
    description: 'Three have been dark since the monsoon.',
    category: 'Infrastructure',
    locality: 'Hiran Magri',
    submittedByMemberId: 'm1',
    status: 'Submitted',
    isMine: false,
    availableTransitions: ['UnderReview'],
    canDecide: false,
    createdAt: '2026-08-01T10:00:00Z',
    decision: null,
    monitoringGroupId: null,
    decidedAt: null,
    ...overrides,
  };
}

function access(overrides: Partial<IssueReviewAccess> = {}): IssueReviewAccess {
  return { canReview: true, canSeeQueue: true, panelSeated: false, isPanelMember: false, ...overrides };
}

describe('IssueQueueComponent', () => {
  let fixture: ComponentFixture<IssueQueueComponent>;
  let component: IssueQueueComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [IssueQueueComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(IssueQueueComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  const buttons = () =>
    [...fixture.nativeElement.querySelectorAll('.row-actions button')].map((b) =>
      (b as HTMLElement).textContent?.trim(),
    );

  function load(issues: SocialIssue[] = [], grant: IssueReviewAccess = access()) {
    fixture.detectChanges();
    http.expectOne('/v1/social-issues/review-access').flush(grant);
    http.expectOne('/v1/social-issues/approval-queue').flush(issues);
    fixture.detectChanges();
  }

  it('reads a 404 as the module being off', () => {
    fixture.detectChanges();
    http.expectOne('/v1/social-issues/review-access').flush({}, { status: 404, statusText: 'Not Found' });
    http
      .expectOne('/v1/social-issues/approval-queue')
      .flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('does not run the social issues module');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('says nothing is waiting rather than showing an empty list', () => {
    load();

    expect(text()).toContain('Nothing waiting');
  });

  it('renders exactly the transitions the server offered', () => {
    // The buttons come from the domain's transition table filtered by this
    // caller's permission. social-issues-service is the one service here whose
    // workflow branches, so deriving the buttons from the status would be a
    // second copy of it — the copy that drifts.
    load([issue({ status: 'UnderReview', availableTransitions: ['DecisionPending', 'Rejected'] })]);

    expect(buttons()).toEqual(['Put in front of the Panch', 'Reject']);
  });

  it('offers nothing on an issue this caller cannot move', () => {
    load([issue({ availableTransitions: [] })]);

    expect(text()).toContain('Nothing for you to decide');
    expect(buttons()).toEqual([]);
  });

  it('will not reject without a reason', () => {
    // The service requires one where a member will ask why. Sending the move
    // without it would be a 409 that reads as a bug.
    load([issue({ status: 'UnderReview', availableTransitions: ['DecisionPending', 'Rejected'] })]);

    component.reason['i1'] = '   ';
    component.move(issue(), 'Rejected');

    http.expectNone('/v1/social-issues/i1/status');
  });

  it('puts an issue in front of the Panch without needing a reason', () => {
    load([issue({ status: 'UnderReview', availableTransitions: ['DecisionPending', 'Rejected'] })]);

    component.move(issue(), 'DecisionPending');

    const call = http.expectOne('/v1/social-issues/i1/status');

    expect(call.request.body).toEqual({ status: 'DecisionPending', reason: null });

    call.flush({});
    reload();
  });

  it('sends the reason, trimmed, on a refusing move', () => {
    load([issue({ status: 'UnderReview', availableTransitions: ['Rejected'] })]);

    component.reason['i1'] = '  Already reported to the municipality.  ';
    component.move(issue(), 'Rejected');

    const call = http.expectOne('/v1/social-issues/i1/status');

    expect(call.request.body).toEqual({
      status: 'Rejected',
      reason: 'Already reported to the municipality.',
    });

    call.flush({});
    reload();
  });

  it('asks for a reason only where one of the moves needs it', () => {
    load([issue({ status: 'Submitted', availableTransitions: ['UnderReview'] })]);

    expect(text()).not.toContain('Reason');

    load2([issue({ status: 'UnderReview', availableTransitions: ['DecisionPending', 'Rejected'] })]);

    expect(text()).toContain('Reason');
  });

  it('shows a recorded decision read-only, and offers no buttons', () => {
    load([
      issue({
        status: 'Closed',
        availableTransitions: [],
        decision: 'Repaint the crossing.',
        decidedAt: '2026-09-01T00:00:00Z',
      }),
    ]);

    expect(text()).toContain("The Panch's decision:");
    expect(text()).toContain('Repaint the crossing.');
    expect(buttons()).toEqual([]);
  });

  it('tells an administrator the Panch has taken over once one is seated', () => {
    load(
      [issue({ status: 'DecisionPending', availableTransitions: [] })],
      access({ canReview: false, panelSeated: true }),
    );

    expect(text()).toContain('Only its members can act on issues now');
  });

  it('says nothing extra while no Panch is seated', () => {
    load([issue({ status: 'Submitted', availableTransitions: ['UnderReview'] })], access());

    expect(text()).not.toContain('Only its members can act on issues now');
  });

  function load2(issues: SocialIssue[]) {
    component.ngOnInit();
    http.expectOne('/v1/social-issues/review-access').flush(access());
    http.expectOne('/v1/social-issues/approval-queue').flush(issues);
    fixture.detectChanges();
  }

  function reload() {
    http.expectOne('/v1/social-issues/review-access').flush(access());
    http.expectOne('/v1/social-issues/approval-queue').flush([]);
    fixture.detectChanges();
  }
});
