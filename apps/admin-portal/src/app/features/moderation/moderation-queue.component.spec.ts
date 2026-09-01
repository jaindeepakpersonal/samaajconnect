import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { ModerationQueueComponent } from './moderation-queue.component';
import { ModerationQueueEntry } from '../../core/admin.models';

function entry(overrides: Partial<ModerationQueueEntry> = {}): ModerationQueueEntry {
  return {
    post: {
      id: 'p1',
      authorMemberId: 'm1',
      type: 'MemberPost',
      title: 'Community Seva Drive',
      body: 'Sunday, 8am at the hall.',
      status: 'PendingReview',
      reportCount: 0,
      commentCount: 0,
      createdAt: '2026-01-01T10:00:00Z',
      moderatedAt: null,
    },
    history: [],
    availableDecisions: ['Approve', 'Reject'],
    ...overrides,
  };
}

describe('ModerationQueueComponent', () => {
  let fixture: ComponentFixture<ModerationQueueComponent>;
  let component: ModerationQueueComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ModerationQueueComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(ModerationQueueComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);

  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;
  const buttons = () =>
    Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>);

  function load(entries: ModerationQueueEntry[], members: { id: string; fullName: string }[] = []) {
    fixture.detectChanges();
    http.expectOne('/v1/members?limit=100').flush(members);
    http.expectOne('/v1/timeline/posts/moderation-queue').flush(entries);
    fixture.detectChanges();
  }

  it('says the queue is empty rather than showing nothing at all', () => {
    load([]);

    expect(text()).toContain('Nothing is waiting');
  });

  it('renders exactly the decisions the server offered', () => {
    // Never derived from the status. A state added to the domain must not leave
    // this panel offering buttons that do nothing.
    load([entry({ availableDecisions: ['Approve', 'Reject'] })]);

    expect(buttons().map((b) => b.textContent?.trim())).toEqual(['Approve', 'Reject']);
  });

  it('offers only Take down for a reported post that is already published', () => {
    load([
      entry({
        post: { ...entry().post, status: 'Approved', reportCount: 3 },
        availableDecisions: ['Hide'],
      }),
    ]);

    expect(buttons().map((b) => b.textContent?.trim())).toEqual(['Take down']);
    expect(text()).toContain('Reported 3 times');
  });

  it('will not send a rejection without a reason, and says so', () => {
    load([entry()]);

    buttons().find((b) => b.textContent?.includes('Reject'))!.click();
    fixture.detectChanges();

    http.expectNone('/v1/timeline/posts/p1/moderate');
    expect(text()).toContain('Say why');
  });

  it('sends the reason with a rejection', () => {
    load([entry()]);

    component.reasons['p1'] = 'Off topic for the Samaaj timeline.';
    fixture.detectChanges();

    buttons().find((b) => b.textContent?.includes('Reject'))!.click();

    const request = http.expectOne('/v1/timeline/posts/p1/moderate');

    expect(request.request.body).toEqual({
      decision: 'Reject',
      reason: 'Off topic for the Samaaj timeline.',
    });

    request.flush({});
    http.expectOne('/v1/timeline/posts/moderation-queue').flush([]);
  });

  it('approves without asking for a reason', () => {
    load([entry()]);

    buttons().find((b) => b.textContent?.trim() === 'Approve')!.click();

    const request = http.expectOne('/v1/timeline/posts/p1/moderate');

    expect(request.request.body).toEqual({ decision: 'Approve', reason: null });

    request.flush({});
    http.expectOne('/v1/timeline/posts/moderation-queue').flush([]);
    fixture.detectChanges();

    expect(text()).toContain('is now on the timeline');
  });

  it('re-reads the queue after a decision rather than dropping the row', () => {
    // Approving a reported post leaves it in the queue; hiding it does not.
    // Only the server knows which, so the screen asks again.
    load([entry()]);

    buttons().find((b) => b.textContent?.trim() === 'Approve')!.click();
    http.expectOne('/v1/timeline/posts/p1/moderate').flush({});

    http.expectOne('/v1/timeline/posts/moderation-queue').flush([entry()]);
    fixture.detectChanges();

    expect(text()).toContain('Community Seva Drive');
  });

  it('puts a name to the author when the directory can supply one', () => {
    load([entry()], [{ id: 'm1', fullName: 'Ravi Shah' }]);

    expect(text()).toContain('Ravi Shah');
  });

  it('says "A member" rather than printing an id it could not resolve', () => {
    load([entry()], []);

    expect(text()).toContain('A member');
    expect(text()).not.toContain('m1');
  });

  it('explains a 404 as the module being off, not as an error', () => {
    // The gateway answers 404 for a Samaaj that has switched `community` off,
    // so a Samaaj without the module is indistinguishable from a platform with
    // no such feature. Reporting it as a failure sends an admin hunting a bug.
    fixture.detectChanges();
    http.expectOne('/v1/members?limit=100').flush([]);
    http
      .expectOne('/v1/timeline/posts/moderation-queue')
      .flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('does not run the community module');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('shows a real failure as an error', () => {
    fixture.detectChanges();
    http.expectOne('/v1/members?limit=100').flush([]);
    http
      .expectOne('/v1/timeline/posts/moderation-queue')
      .flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
  });
});
