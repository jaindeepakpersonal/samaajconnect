import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { IssueQueueComponent } from './issue-queue.component';
import { SocialIssue } from '../../core/admin.models';

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
    createdAt: '2026-08-01T10:00:00Z',
    publishedAt: null,
    ...overrides,
  };
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

  function load(issues: SocialIssue[] = []) {
    fixture.detectChanges();
    http.expectOne('/v1/social-issues/approval-queue').flush(issues);
    fixture.detectChanges();
  }

  it('reads a 404 as the module being off', () => {
    fixture.detectChanges();
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
    load([issue({ status: 'UnderReview', availableTransitions: ['Approved', 'Rejected'] })]);

    expect(buttons()).toEqual(['Approve', 'Reject']);
  });

  it('offers nothing on an issue this caller cannot move', () => {
    load([issue({ availableTransitions: [] })]);

    expect(text()).toContain('Nothing for you to decide');
    expect(buttons()).toEqual([]);
  });

  it('will not reject without a reason', () => {
    // The service requires one where a member will ask why. Sending the move
    // without it would be a 409 that reads as a bug.
    load([issue({ status: 'UnderReview', availableTransitions: ['Approved', 'Rejected'] })]);

    component.reason['i1'] = '   ';
    component.move(issue(), 'Rejected');

    http.expectNone('/v1/social-issues/i1/status');
  });

  it('approves without needing one', () => {
    load([issue({ status: 'UnderReview', availableTransitions: ['Approved', 'Rejected'] })]);

    component.move(issue(), 'Approved');

    const call = http.expectOne('/v1/social-issues/i1/status');

    expect(call.request.body).toEqual({ status: 'Approved', reason: null });

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

    load2([issue({ status: 'UnderReview', availableTransitions: ['Approved', 'Rejected'] })]);

    expect(text()).toContain('Reason');
  });

  function load2(issues: SocialIssue[]) {
    component.ngOnInit();
    http.expectOne('/v1/social-issues/approval-queue').flush(issues);
    fixture.detectChanges();
  }

  function reload() {
    http.expectOne('/v1/social-issues/approval-queue').flush([]);
    fixture.detectChanges();
  }
});
