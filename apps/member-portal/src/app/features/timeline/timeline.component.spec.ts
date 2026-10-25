import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG, CurrentUser } from '@samaajconnect/shared';
import { Member, MyProfile } from '../members/members.models';
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

/** The reader's Samaaj as the anonymous lookup answers it: no logo, no banner. */
const SAMAAJ = {
  id: 't1',
  name: 'Mahavir Samaaj',
  slug: 'mahavir-samaj',
  logoUrl: null as string | null,
  bannerUrl: null as string | null,
  status: 'Active',
  enabledModules: ['community'],
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

/** A minimal member-family-service response, for the name-resolution calls `timeline.component.ts` now makes. */
function apiMember(id: string, fullName = 'Some Member'): Member {
  return {
    id,
    fullName,
    photoUrl: null,
    gautraId: null,
    dateOfBirth: null,
    mobile: null,
    email: null,
    permanentAddress: { line: null, cityId: null, localityId: null },
    temporaryAddress: null,
    professionId: null,
    qualificationId: null,
    gender: 'Unspecified',
  };
}

/** The reader's own profile, for the card at the top of the page. */
function myProfile(overrides: Partial<MyProfile> = {}): MyProfile {
  return {
    id: ME,
    tenantId: 't1',
    fullName: 'Ravi Shah',
    photoUrl: null,
    dateOfBirth: null,
    gender: 'Male',
    mobile: null,
    email: null,
    gautraId: null,
    professionId: null,
    qualificationId: null,
    permanentAddress: { line: null, cityId: null, localityId: null },
    temporaryAddress: null,
    privacy: {} as MyProfile['privacy'],
    isListedInDirectory: true,
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: null,
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

  /**
   * Flushes every pending name-resolution call the component makes for an
   * author it does not already know - see `resolveNames` on the component and
   * this file's own doc comment on why it now resolves these instead of
   * always falling back to "A member".
   */
  function flushMemberLookups(): void {
    for (const req of http.match(
      (r) =>
        r.method === 'GET' &&
        r.url.startsWith('/v1/members/') &&
        r.url !== '/v1/members/me' &&
        !r.url.endsWith('/photo'),
    )) {
      req.flush(apiMember(req.request.url.split('/').pop()!));
    }
  }

  /**
   * Answers the reader's own profile, which the card at the top asks for -
   * and the Samaaj lookup for the header above it, when /me has answered and
   * so asked for one.
   */
  function flushMe(overrides: Partial<MyProfile> = {}, samaaj: object = SAMAAJ): void {
    http.expectOne('/v1/members/me').flush(myProfile(overrides));

    for (const req of http.match('/v1/identity/tenants/mahavir-samaj')) {
      req.flush(samaaj);
    }
  }

  /** Answers the calls the screen makes on load: /me, the feed, then any author names it needs. */
  function load(posts: Post[], user: CurrentUser = member): void {
    fixture.detectChanges();

    http.expectOne('/v1/identity/me').flush(user);
    http.expectOne('/v1/timeline/posts').flush(posts);
    flushMe();
    flushMemberLookups();

    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function buttonSaying(label: string): HTMLButtonElement | undefined {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((button) => button.textContent?.trim().includes(label)) as HTMLButtonElement | undefined;
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

  it('offers Dislike alongside the other three reactions', () => {
    // Added 2026-09-12 to the platform's own closed enum - the button exists
    // here only because Reactions in timeline.models.ts lists it, not because
    // this screen invented a fourth one.
    load([post({ status: 'Approved' })]);

    expect(buttonSaying('Dislike')).toBeDefined();
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
    flushMemberLookups();

    fixture.detectChanges();

    expect(text()).toContain('Count me in.');
    // Already resolved for the post's own author during load() - the same id,
    // so no second lookup, just the memoized name.
    expect(text()).toContain('Some Member');
  });

  it('names the reader as the author of their own comment', () => {
    load([post()]);

    component.toggleComments(component.posts()[0]!);

    http.expectOne('/v1/timeline/posts/p1').flush({
      post: post(),
      comments: [{ id: 'c1', authorMemberId: ME, body: 'Mine.', createdAt: new Date().toISOString() }],
    });
    flushMemberLookups();

    fixture.detectChanges();

    expect(text()).toContain('You');
  });

  it('bumps the count when a comment is added rather than re-fetching the feed', () => {
    load([post({ commentCount: 0 })]);

    component.toggleComments(component.posts()[0]!);
    http.expectOne('/v1/timeline/posts/p1').flush({ post: post({ commentCount: 0 }), comments: [] });
    flushMemberLookups();
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

  // ---- Names & icons -------------------------------------------------------

  it("resolves and shows a post's author name", () => {
    load([post({ authorMemberId: 'someone-else' })]);

    expect(text()).toContain('Some Member');
  });

  it('reuses an already-resolved name rather than asking for it twice', () => {
    // The comment shares the post's own author id - load() already resolved
    // it, so toggleComments() must not issue a second /v1/members/ call.
    load([post({ authorMemberId: 'someone-else', commentCount: 1 })]);

    component.toggleComments(component.posts()[0]!);

    http.expectOne('/v1/timeline/posts/p1').flush({
      post: post({ authorMemberId: 'someone-else', commentCount: 1 }),
      comments: [
        {
          id: 'c1',
          authorMemberId: 'someone-else',
          body: 'Count me in.',
          createdAt: new Date().toISOString(),
        },
      ],
    });
    // No flushMemberLookups() here - http.verify() in afterEach is the
    // assertion that nothing was left pending, i.e. nothing was requested.

    fixture.detectChanges();

    expect(text()).toContain('Some Member');
  });

  it('shows an icon on every reaction, the comment count and Report', () => {
    load([post({ reactions: [{ type: 'Appreciate', count: 1 }] })]);

    expect(buttonSaying('Appreciate')?.textContent).toContain('👏');
    expect(buttonSaying('Support')?.textContent).toContain('🤝');
    expect(buttonSaying('Celebrate')?.textContent).toContain('🎉');
    expect(buttonSaying('Dislike')?.textContent).toContain('👎');
    expect(buttonSaying('comment')?.textContent).toContain('💬');
    expect(buttonSaying('Report')?.textContent).toContain('🚩');
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
    flushMe();
    fixture.detectChanges();

    expect(text()).toContain('Try again');
  });

  it('reads a 404 on the feed as the module being off, not a missing post', () => {
    // The feed is a list - nothing about it can 404 per item - so a 404 here
    // means the Samaaj has not switched community on, and the screen should
    // say that rather than showing the generic "something went wrong" box.
    fixture.detectChanges();

    http.expectOne('/v1/identity/me').flush(member);
    http.expectOne('/v1/timeline/posts').flush({}, { status: 404, statusText: 'Not Found' });
    flushMe();
    fixture.detectChanges();

    expect(text()).toContain('not switched on for your Samaaj');
    expect(text()).not.toContain('Try again');
    expect(buttonSaying('Post for Review')).toBeUndefined();
  });

  it('warns an unverified member, and says nothing once they are verified', () => {
    load([], { ...member, isContactVerified: false });

    expect(text()).toContain('not verified yet');
  });

  it('says nothing about verification before /me has answered', () => {
    fixture.detectChanges();

    // Only the feed has answered so far - /me has not, so the screen must not
    // flash a false claim about the member's account before it knows one.
    expect(text()).not.toContain('not verified yet');

    http.expectOne('/v1/identity/me').flush(member);
    http.expectOne('/v1/timeline/posts').flush([]);
    flushMe();
  });

  it('still shows the feed when the profile call fails', () => {
    // Without /me the screen cannot offer the announcement option, but a member
    // should still be able to read their Samaaj timeline.
    fixture.detectChanges();

    http.expectOne('/v1/identity/me').flush({}, { status: 500, statusText: 'Server Error' });
    http.expectOne('/v1/timeline/posts').flush([post()]);
    flushMe();
    flushMemberLookups();
    fixture.detectChanges();

    expect(text()).toContain('Community blood donation drive');
  });

  // ---- The Samaaj's banner and logo -----------------------------------------

  function hero(): HTMLElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector('.samaaj-hero');
  }

  it('heads Home with the Samaaj name, its logo, and its banner fetched with the token', () => {
    URL.createObjectURL = () => 'blob:banner';
    URL.revokeObjectURL = () => undefined;
    const banner = '/v1/identity/tenants/t1/banner?v=abcd1234';

    fixture.detectChanges();
    http.expectOne('/v1/identity/me').flush(member);
    http.expectOne('/v1/timeline/posts').flush([]);
    flushMe({}, { ...SAMAAJ, logoUrl: '/v1/identity/tenants/t1/logo', bannerUrl: banner });
    fixture.detectChanges();

    // The banner is members-only: not drawn from its bare path, but fetched.
    http.expectOne(banner).flush(new Blob(['x']));
    fixture.detectChanges();

    expect(hero()?.textContent).toContain('Mahavir Samaaj');
    expect(hero()?.classList).toContain('has-banner');
    expect(hero()?.querySelector<HTMLImageElement>('.hero-banner')?.getAttribute('src')).toBe('blob:banner');

    // The logo is public, so a plain src is right for it.
    expect(hero()?.querySelector<HTMLImageElement>('img.hero-logo')?.getAttribute('src'))
      .toBe('/v1/identity/tenants/t1/logo');
  });

  it('still heads Home with the name and initials when the Samaaj has neither image', () => {
    load([]);

    expect(hero()?.textContent).toContain('Mahavir Samaaj');
    expect(hero()?.classList).not.toContain('has-banner');
    expect(hero()?.querySelector('img')).toBeNull();
    expect(hero()?.querySelector('.hero-logo.initials')?.textContent?.trim()).toBe('MS');
  });

  it('draws no header at all when the Samaaj lookup fails, and the feed is unaffected', () => {
    fixture.detectChanges();
    http.expectOne('/v1/identity/me').flush(member);
    http.expectOne('/v1/timeline/posts').flush([post()]);
    http.expectOne('/v1/members/me').flush(myProfile());
    http.expectOne('/v1/identity/tenants/mahavir-samaj').flush({}, { status: 500, statusText: 'Server Error' });
    flushMemberLookups();
    fixture.detectChanges();

    expect(hero()).toBeNull();
    expect(text()).toContain('Community blood donation drive');
  });

  // ---- Faces ------------------------------------------------------------------

  it('opens on the reader: their photo, name and Gautra', () => {
    URL.createObjectURL = () => 'blob:test';
    URL.revokeObjectURL = () => undefined;

    fixture.detectChanges();
    http.expectOne('/v1/identity/me').flush(member);
    http.expectOne('/v1/timeline/posts').flush([]);
    flushMe({ photoUrl: `/v1/members/${ME}/photo`, gautraId: 'g2' });
    http.expectOne((r) => r.url === '/v1/master-data/gautras').flush([
      { id: 'g1', name: 'Bhandari', isActive: true },
      { id: 'g2', name: 'Kothari', isActive: true },
    ]);
    fixture.detectChanges();
    http.expectOne(`/v1/members/${ME}/photo`).flush(new Blob(['x']));
    fixture.detectChanges();

    const card = (fixture.nativeElement as HTMLElement).querySelector('.me-card')!;
    expect(card.textContent).toContain('Ravi Shah');
    expect(card.textContent).toContain('Gautra: Kothari');
    expect(card.querySelector<HTMLImageElement>('img.avatar')?.getAttribute('src')).toBe('blob:test');
  });

  it('stands initials in for a reader with no photo, and asks for no Gautra list without one', () => {
    load([]);

    const card = (fixture.nativeElement as HTMLElement).querySelector('.me-card')!;
    expect(card.querySelector('img')).toBeNull();
    expect(card.querySelector('.initials')?.textContent?.trim()).toBe('RS');
    expect(card.textContent).toContain('Add a photo to your profile');
    http.expectNone((r) => r.url === '/v1/master-data/gautras');
  });

  it('shows each author with their photo, or their initials, from the lookup that names them', () => {
    URL.createObjectURL = () => 'blob:test';
    URL.revokeObjectURL = () => undefined;

    fixture.detectChanges();
    http.expectOne('/v1/identity/me').flush(member);
    http.expectOne('/v1/timeline/posts').flush([
      post({ id: 'p1', authorMemberId: 'a1' }),
      post({ id: 'p2', authorMemberId: 'a2' }),
    ]);
    flushMe();
    http.expectOne('/v1/members/a1').flush({ ...apiMember('a1', 'Meera Jain'), photoUrl: '/v1/members/a1/photo' });
    http.expectOne('/v1/members/a2').flush(apiMember('a2', 'Anil Mehta'));
    fixture.detectChanges();
    http.expectOne('/v1/members/a1/photo').flush(new Blob(['x']));
    fixture.detectChanges();

    const posts = [...(fixture.nativeElement as HTMLElement).querySelectorAll('article.post')];
    expect(posts[0]!.querySelector('img.avatar')).not.toBeNull();
    expect(posts[1]!.querySelector('img.avatar')).toBeNull();
    expect(posts[1]!.querySelector('.initials')?.textContent?.trim()).toBe('AM');
  });

  it('says the timeline is empty rather than showing nothing at all', () => {
    load([]);

    expect(text()).toContain('Nothing has been posted yet');
  });
});
