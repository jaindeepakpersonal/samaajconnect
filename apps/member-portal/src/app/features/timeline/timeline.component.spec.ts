import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG, CurrentUser } from '@samaajconnect/shared';
import { TimelineComponent } from './timeline.component';
import { Post } from './timeline.models';

const ME = 'u1';

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
  permissions: ['Members.Read', 'Timeline.Post'],
};

function post(overrides: Partial<Post> = {}): Post {
  return {
    id: 'p1',
    authorMemberId: 'someone-else',
    type: 'MemberPost',
    title: 'Community blood donation drive',
    body: 'Volunteers are welcome to participate.',
    status: 'Approved',
    reportCount: 0,
    reactions: [],
    myReaction: null,
    commentCount: 0,
    createdAt: new Date().toISOString(),
    moderatedAt: null,
    ...overrides,
  };
}

describe('TimelineComponent', () => {
  let fixture: ComponentFixture<TimelineComponent>;
  let component: TimelineComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TimelineComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(TimelineComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** Answers the two calls the screen makes on load. */
  function load(posts: Post[], user: CurrentUser = member): void {
    fixture.detectChanges();

    http.expectOne('/v1/identity/me').flush(user);
    http.expectOne('/v1/timeline/posts').flush(posts);

    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function buttonSaying(label: string): HTMLButtonElement | undefined {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((button) => button.textContent?.trim().startsWith(label)) as HTMLButtonElement | undefined;
  }

  // ---- The three states the wireframe shows -----------------------------

  it('labels a Samaaj announcement, a member post and the reader own post', () => {
    load([
      post({ id: 'a', type: 'Announcement', authorMemberId: 'admin', title: 'Paryushan' }),
      post({ id: 'b', type: 'MemberPost', authorMemberId: 'someone-else' }),
      post({ id: 'c', type: 'MemberPost', authorMemberId: ME, status: 'PendingReview' }),
    ]);

    expect(text()).toContain('Samaaj announcement');
    expect(text()).toContain('Member post');
    expect(text()).toContain('Your post • Awaiting review');
  });

  it('tells the author that a pending post is theirs alone to see', () => {
    load([post({ authorMemberId: ME, status: 'PendingReview' })]);

    expect(text()).toContain('Only you can see this');
  });

  it('offers no reactions or reports on a post nobody else can see', () => {
    // Reacting to a post that is not public is meaningless, and offering it
    // would suggest the post had been published.
    load([post({ authorMemberId: ME, status: 'PendingReview' })]);

    expect(buttonSaying('Appreciate')).toBeUndefined();
    expect(buttonSaying('Report')).toBeUndefined();
  });

  it('does not offer a member the chance to report their own post', () => {
    load([post({ authorMemberId: ME, status: 'Approved' })]);

    expect(buttonSaying('Appreciate')).toBeDefined();
    expect(buttonSaying('Report')).toBeUndefined();
  });

  // ---- Composing ---------------------------------------------------------

  it('says a member post goes for review rather than implying it is live', () => {
    load([]);

    expect(buttonSaying('Post for Review')).toBeDefined();

    component.title = 'Looking for Pathshala volunteers';
    component.body = 'Sunday mornings.';
    component.submit();

    const request = http.expectOne('/v1/timeline/posts');

    expect(request.request.body).toEqual({
      title: 'Looking for Pathshala volunteers',
      body: 'Sunday mornings.',
      asAnnouncement: false,
    });

    request.flush(post({ id: 'new', authorMemberId: ME, status: 'PendingReview' }));
    fixture.detectChanges();

    expect(text()).toContain('gone to the moderators');
  });

  it('shows the new post straight away instead of re-fetching the feed', () => {
    load([post({ id: 'old' })]);

    component.title = 'A title';
    component.body = 'A body.';
    component.submit();

    http.expectOne('/v1/timeline/posts').flush(post({ id: 'new', authorMemberId: ME }));
    fixture.detectChanges();

    expect(component.posts().map((p) => p.id)).toEqual(['new', 'old']);
  });

  it('offers the announcement option only to somebody who may moderate', () => {
    load([]);

    expect(text()).not.toContain('without review');
  });

  it('and offers it to somebody who may', () => {
    load([], { ...member, permissions: [...member.permissions, 'Timeline.Moderate'] });

    expect(text()).toContain('without review');
  });

  it('keeps what was typed when posting fails', () => {
    load([]);

    component.title = 'A title';
    component.body = 'A body.';
    component.submit();

    http
      .expectOne('/v1/timeline/posts')
      .flush({ title: 'Timeline.Closed', detail: 'No.' }, { status: 409, statusText: 'Conflict' });

    fixture.detectChanges();

    // Clearing the box on failure loses what somebody wrote.
    expect(component.title).toBe('A title');
    expect(component.composeError()).not.toBeNull();
  });

  // ---- Reacting ----------------------------------------------------------

  it('sends the reaction and takes the server answer for the new state', () => {
    // The server decides whether a tap sets or clears, because sending the
    // reaction you already hold removes it.
    load([post({ myReaction: 'Appreciate', reactions: [{ type: 'Appreciate', count: 1 }] })]);

    component.react(component.posts()[0]!, 'Appreciate');

    const request = http.expectOne('/v1/timeline/posts/p1/reaction');

    expect(request.request.body).toEqual({ reaction: 'Appreciate' });

    request.flush(post({ myReaction: null, reactions: [] }));
    fixture.detectChanges();

    expect(component.posts()[0]!.myReaction).toBeNull();
  });

  it('keeps a failed reaction off the page-level error', () => {
    // A reaction that failed should not replace the feed with an error box.
    load([post()]);

    component.react(component.posts()[0]!, 'Appreciate');

    http
      .expectOne('/v1/timeline/posts/p1/reaction')
      .flush({}, { status: 500, statusText: 'Server Error' });

    fixture.detectChanges();

    expect(component.error()).toBeNull();
    expect(component.postError()['p1']).toBeDefined();
  });

  // ---- Comments ----------------------------------------------------------

  it('loads comments only when they are asked for', () => {
    load([post({ commentCount: 1 })]);

    component.toggleComments(component.posts()[0]!);

    http.expectOne('/v1/timeline/posts/p1').flush({
      post: post({ commentCount: 1 }),
      comments: [
        { id: 'c1', authorMemberId: 'someone-else', body: 'Count me in.', createdAt: new Date().toISOString() },
      ],
    });

    fixture.detectChanges();

    expect(text()).toContain('Count me in.');
    expect(text()).toContain('A member');
  });

  it('names the reader as the author of their own comment', () => {
    load([post()]);

    component.toggleComments(component.posts()[0]!);

    http.expectOne('/v1/timeline/posts/p1').flush({
      post: post(),
      comments: [{ id: 'c1', authorMemberId: ME, body: 'Mine.', createdAt: new Date().toISOString() }],
    });

    fixture.detectChanges();

    expect(text()).toContain('You');
  });

  it('bumps the count when a comment is added rather than re-fetching the feed', () => {
    load([post({ commentCount: 0 })]);

    component.toggleComments(component.posts()[0]!);
    http.expectOne('/v1/timeline/posts/p1').flush({ post: post({ commentCount: 0 }), comments: [] });
    fixture.detectChanges();

    component.draftComment = 'Well said.';
    component.addComment(component.posts()[0]!);

    http
      .expectOne('/v1/timeline/posts/p1/comments')
      .flush({ id: 'c9', authorMemberId: ME, body: 'Well said.', createdAt: new Date().toISOString() });

    fixture.detectChanges();

    expect(component.posts()[0]!.commentCount).toBe(1);
    expect(component.draftComment).toBe('');
  });

  // ---- Reporting ---------------------------------------------------------

  it('says a report landed without pretending the post changed', () => {
    load([post()]);

    component.report(component.posts()[0]!);

    http
      .expectOne('/v1/timeline/posts/p1/report')
      .flush({ postId: 'p1', message: 'Thank you. A moderator will look at this.' });

    fixture.detectChanges();

    // Reporting removes nothing by itself, so the post is still there.
    expect(component.posts()).toHaveLength(1);
    expect(buttonSaying('Reported')).toBeDefined();
  });

  // ---- What is not built -------------------------------------------------

  it('disables Attach Photo and says why instead of faking an upload', () => {
    load([]);

    const attach = buttonSaying('Attach Photo');

    expect(attach?.disabled).toBe(true);
    expect(text()).toContain('no file storage');
  });

  // ---- Failure -----------------------------------------------------------

  it('offers a retry when the feed cannot be loaded', () => {
    fixture.detectChanges();

    http.expectOne('/v1/identity/me').flush(member);
    http.expectOne('/v1/timeline/posts').flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();

    expect(text()).toContain('Try again');
  });

  it('still shows the feed when the profile call fails', () => {
    // Without /me the screen cannot offer the announcement option, but a member
    // should still be able to read their Samaaj timeline.
    fixture.detectChanges();

    http.expectOne('/v1/identity/me').flush({}, { status: 500, statusText: 'Server Error' });
    http.expectOne('/v1/timeline/posts').flush([post()]);
    fixture.detectChanges();

    expect(text()).toContain('Community blood donation drive');
  });

  it('says the timeline is empty rather than showing nothing at all', () => {
    load([]);

    expect(text()).toContain('Nothing has been posted yet');
  });
});
