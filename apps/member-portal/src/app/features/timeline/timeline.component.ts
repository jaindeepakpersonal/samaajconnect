import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService, AuthedImageDirective, describeError } from '@samaajconnect/shared';
import { MasterDataApi } from '../members/master-data.api';
import { MembersApi } from '../members/members.api';
import { MyProfile } from '../members/members.models';
import { TimelineApi } from './timeline.api';
import { Comment, Post, ReactionType, Reactions } from './timeline.models';

/**
 * Timeline, from the member-portal wireframe's `#timeline` screen.
 *
 * This is also where a member lands after signing in, and on `/` - see
 * apps/member-portal/CLAUDE.md. There used to be a Home in between, a grid of
 * tiles for every route already on the sidebar; it carried three notices this
 * screen has kept (contact verification below, and the module-disabled state
 * this screen's own 404 handling covers) and nothing a member could not
 * already reach from the nav.
 *
 * `community`, Timeline's own module, can be switched off for a Samaaj that
 * has landed here anyway - the gateway answers 404 for the feed, not a
 * missing post, since a feed endpoint has no per-item 404 of its own. `load()`
 * tells the two apart and this screen says so plainly rather than showing the
 * generic error box a member has no way to act on.
 *
 * The wireframe shows three posts in three states - a Samaaj announcement, an
 * approved member post with a Report button, and the reader's own post awaiting
 * moderation. Those are the three real branches this screen has to render, and
 * they are driven by `type`, `status` and whether the author is the reader,
 * not by a static label.
 *
 * Two things the wireframe shows are not built, and are not faked.
 *
 * **Attach Photo.** There is no upload endpoint and no file storage on the
 * platform (`DEVELOPMENT_PLAN.md` Phase 5, "Platform-hosted images"). The
 * button is present and disabled with the reason beside it, rather than wired
 * to something that does not exist.
 *
 * **Author names.** A post carries `authorMemberId` and no name - resolving
 * one is a call per author, not per post, so `resolveNames()` batches it: one
 * `MembersApi.get()` per distinct id actually on screen (the loaded page of
 * the feed, or the open comment thread), memoized in `names()` and never
 * repeated. The reader's relationship to the post ("Your post", "Samaaj
 * announcement", "Member post") is still the meta pill; the name sits beside
 * it as who, not what kind of post this is.
 *
 * Reactions and comments are additions, not wireframe elements: the endpoints
 * exist and a timeline with no way to respond is a noticeboard.
 *
 * **Faces, since 2026-09-27.** The page opens on the reader's own card - their
 * photo, name and Gautra, the way a social feed greets its reader - and every
 * post and comment carries its author's photo. Photos are the platform's own,
 * fetched with the token through `scAuthedSrc`; a member with none, or one
 * whose photo is not visible to this reader, gets their initials instead. The
 * author's photo path comes from the same one `MembersApi.get()` per author
 * that already resolves their name, so faces cost no lookup names did not.
 */
@Component({
  selector: 'app-timeline',
  imports: [FormsModule, RouterLink, AuthedImageDirective],
  styleUrl: './timeline.css',
  template: `
    <div class="timeline-page">
      <header class="page-head">
        <div>
          <h1 class="page-title">Timeline</h1>
          <p class="subtitle">Samaaj announcements and approved public member posts.</p>
        </div>
      </header>

      <section class="card me-card" aria-label="You">
        @if (myPhoto(); as path) {
          <img class="avatar large" [scAuthedSrc]="path" alt="" />
        } @else {
          <span class="avatar large initials" aria-hidden="true">{{ initials(myName()) }}</span>
        }
        <div class="me-text">
          <p class="me-name">{{ myName() }}</p>
          @if (gautraName(); as gautra) {
            <p class="small">Gautra: {{ gautra }}</p>
          }
          <a class="small" routerLink="/profile">{{ myProfile()?.photoUrl ? 'Edit your profile' : 'Add a photo to your profile' }}</a>
        </div>
      </section>

      @if (!isContactVerified()) {
        <p class="notice info" role="status">
          Your mobile number is not verified yet. Verification messages are not switched on yet;
          nothing is blocked in the meantime.
        </p>
      }

      @if (moduleDisabled()) {
        <p class="notice info" role="status">
          Timeline is not switched on for your Samaaj yet. Your administrator can enable it.
        </p>
      } @else {
        <!-- Compose ----------------------------------------------------- -->
        <form class="card compose" (ngSubmit)="submit()">
          <label class="sr-only" for="post-title">Title</label>
          <input
            class="input"
            id="post-title"
            name="title"
            [(ngModel)]="title"
            placeholder="A short title"
            maxlength="200"
            required
          />

          <label class="sr-only" for="post-body">Your post</label>
          <textarea
            class="input"
            id="post-body"
            name="body"
            [(ngModel)]="body"
            placeholder="Share something with the Samaaj..."
            rows="3"
            maxlength="5000"
            required
          ></textarea>

          @if (canModerate()) {
            <label class="announce">
              <input type="checkbox" name="announce" [(ngModel)]="asAnnouncement" />
              Publish as a Samaaj announcement, without review
            </label>
          }

          @if (composeError(); as message) {
            <p class="notice error" role="alert">{{ message }}</p>
          }

          @if (justPosted()) {
            <p class="notice info" role="status">
              {{
                asAnnouncement
                  ? 'Your announcement is live.'
                  : 'Your post has gone to the moderators. It appears below, marked as awaiting review.'
              }}
            </p>
          }

          <div class="actions">
            <button class="btn secondary" type="button" disabled>Attach Photo</button>
            <span class="small">
              Photos are not switched on yet - the platform has no file storage.
            </span>

            <button class="btn" type="submit" [disabled]="!canPost() || posting()">
              {{ posting() ? 'Posting…' : asAnnouncement ? 'Publish' : 'Post for Review' }}
            </button>
          </div>
        </form>

        <!-- The feed ----------------------------------------------------- -->
        @if (loading()) {
          <p role="status">Loading the timeline…</p>
        } @else if (error(); as message) {
          <div class="notice error" role="alert">
            {{ message }}
            <button class="btn link" type="button" (click)="load()">Try again</button>
          </div>
        } @else if (posts().length === 0) {
          <p class="notice info" role="status">
            Nothing has been posted yet. Yours would be the first.
          </p>
        } @else {
          <div class="feed">
            @for (post of posts(); track post.id) {
              <article class="post card">
                <div class="meta">
                  @if (photoOf(post.authorMemberId); as path) {
                    <img class="avatar" [scAuthedSrc]="path" alt="" />
                  } @else {
                    <span class="avatar initials" aria-hidden="true">{{ initials(authorLabel(post.authorMemberId)) }}</span>
                  }
                  <span class="author">{{ authorLabel(post.authorMemberId) }}</span>
                  <span class="pill" [class.warn]="isAwaitingReview(post)">{{ label(post) }}</span>
                  <span>{{ when(post.createdAt) }}</span>
                </div>

                <h2 class="post-title">{{ post.title }}</h2>
                <p class="post-body">{{ post.body }}</p>

                @if (isAwaitingReview(post)) {
                  <p class="small">
                    Only you can see this. A moderator will decide whether it goes to the Samaaj.
                  </p>
                } @else if (post.status === 'Rejected') {
                  <p class="small">
                    This was not published. Only you can see it.
                  </p>
                }

                @if (isReadable(post)) {
                  <div class="post-actions">
                    @for (reaction of reactions; track reaction.type) {
                      <button
                        class="btn link"
                        type="button"
                        [class.mine]="post.myReaction === reaction.type"
                        [attr.aria-pressed]="post.myReaction === reaction.type"
                        [disabled]="busyId() === post.id"
                        (click)="react(post, reaction.type)"
                      >
                        <span class="icon" aria-hidden="true">{{ reaction.icon }}</span>
                        {{ reaction.label }}
                        @if (countOf(post, reaction.type); as count) {
                          <span class="count">{{ count }}</span>
                        }
                      </button>
                    }

                    <button class="btn link" type="button" (click)="toggleComments(post)">
                      <span class="icon" aria-hidden="true">💬</span>
                      {{ post.commentCount === 1 ? '1 comment' : post.commentCount + ' comments' }}
                    </button>

                    @if (!isMine(post)) {
                      <button
                        class="btn link"
                        type="button"
                        [disabled]="busyId() === post.id || reported().includes(post.id)"
                        (click)="report(post)"
                      >
                        <span class="icon" aria-hidden="true">🚩</span>
                        {{ reported().includes(post.id) ? 'Reported' : 'Report' }}
                      </button>
                    }
                  </div>
                }

                @if (postError()[post.id]; as message) {
                  <p class="notice error" role="alert">{{ message }}</p>
                }

                @if (openComments() === post.id) {
                  <div class="comments">
                    @if (loadingComments()) {
                      <p role="status">Loading comments…</p>
                    } @else {
                      @for (comment of comments(); track comment.id) {
                        <div class="comment">
                          <div class="meta">
                            @if (photoOf(comment.authorMemberId); as path) {
                              <img class="avatar small" [scAuthedSrc]="path" alt="" />
                            } @else {
                              <span class="avatar small initials" aria-hidden="true">{{ initials(authorLabel(comment.authorMemberId)) }}</span>
                            }
                            <span>{{ authorLabel(comment.authorMemberId) }}</span>
                            <span>{{ when(comment.createdAt) }}</span>
                          </div>
                          <p>{{ comment.body }}</p>
                        </div>
                      } @empty {
                        <p class="small">No comments yet.</p>
                      }

                      <form class="add-comment" (ngSubmit)="addComment(post)">
                        <label class="sr-only" [for]="'comment-' + post.id">Add a comment</label>
                        <input
                          class="input"
                          [id]="'comment-' + post.id"
                          name="comment"
                          [(ngModel)]="draftComment"
                          placeholder="Add a comment"
                          maxlength="2000"
                        />
                        <button class="btn" type="submit" [disabled]="!draftComment.trim()">
                          Comment
                        </button>
                      </form>
                    }
                  </div>
                }
              </article>
            }
          </div>
        }
      }
    </div>
  `,
})
export class TimelineComponent implements OnInit {
  private readonly api = inject(TimelineApi);
  private readonly auth = inject(AuthService);
  private readonly members = inject(MembersApi);
  private readonly masterData = inject(MasterDataApi);

  readonly reactions = Reactions;

  /** Resolved author names, keyed by member id. See `resolveNames`. */
  readonly names = signal<Record<string, string>>({});

  /** Authors' photo paths from the same lookups, null for a member with none to show. */
  readonly photos = signal<Record<string, string | null>>({});

  /** The reader's own profile, for the card at the top. Null until it loads, or if it cannot. */
  readonly myProfile = signal<MyProfile | null>(null);

  readonly gautraName = signal<string | null>(null);

  readonly myName = computed(() => this.myProfile()?.fullName ?? this.auth.user()?.fullName ?? 'You');

  readonly myPhoto = computed(() => this.myProfile()?.photoUrl ?? null);

  readonly posts = signal<readonly Post[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  /**
   * True once `load()` has seen a 404 on the feed itself. A feed is a list -
   * nothing about `GET /v1/timeline/posts` can 404 for a per-item reason - so
   * a 404 here means `community` is off for this Samaaj, not that something
   * is missing.
   */
  readonly moduleDisabled = signal(false);

  /** Which post a request is in flight for, so only its buttons disable. */
  readonly busyId = signal<string | null>(null);

  /**
   * Per-post failures, keyed by id. Kept apart from the page-level error: a
   * reaction that failed should not replace the feed with an error box.
   */
  readonly postError = signal<Record<string, string>>({});

  readonly openComments = signal<string | null>(null);
  readonly comments = signal<readonly Comment[]>([]);
  readonly loadingComments = signal(false);

  /** Posts reported in this session, so the button can say so. */
  readonly reported = signal<readonly string[]>([]);

  readonly composeError = signal<string | null>(null);
  readonly justPosted = signal(false);
  readonly posting = signal(false);

  title = '';
  body = '';
  asAnnouncement = false;
  draftComment = '';

  private readonly me = computed(() => this.auth.user()?.userId ?? null);

  /**
   * Whether this member may publish without review. Read from `/me`, not the
   * token, which is why the screen waits for `ensureCurrentUser`.
   */
  readonly canModerate = computed(() =>
    (this.auth.user()?.permissions ?? []).includes('Timeline.Moderate'),
  );

  /**
   * Defaults to true (nothing to warn about) until `/me` has actually loaded,
   * the same reasoning `canModerate` follows - a screen should not flash a
   * false claim about a member's account before it knows anything.
   */
  readonly isContactVerified = computed(() => this.auth.user()?.isContactVerified ?? true);

  /**
   * A method, not a `computed`.
   *
   * `title` and `body` are plain fields bound with `[(ngModel)]`, so a computed
   * over them would read them once and never again - it captured two empty
   * strings at construction and stayed false, which left the Post button
   * permanently disabled. A computed only tracks signals; if these become
   * signals, this can go back to being one.
   */
  canPost(): boolean {
    return this.title.trim().length > 0 && this.body.trim().length > 0;
  }

  ngOnInit(): void {
    // Roles and permissions decide whether the announcement option is offered,
    // and they arrive from /me rather than from the token. Loading the feed
    // first would render the compose box before that is known.
    this.auth.ensureCurrentUser().subscribe({
      next: () => {
        this.load();
        this.loadMe();
      },
      error: () => {
        this.load();
        this.loadMe();
      },
    });
  }

  /**
   * The reader's own card. A failure leaves the name from `/me` and initials -
   * the feed does not depend on it. The Gautra's name is one list, asked for
   * only when the member has picked one.
   */
  private loadMe(): void {
    this.members.me().subscribe({
      next: (profile) => {
        this.myProfile.set(profile);

        if (profile.gautraId) {
          this.masterData.listGautras().subscribe({
            next: (gautras) => this.gautraName.set(gautras.find((g) => g.id === profile.gautraId)?.name ?? null),
            error: () => this.gautraName.set(null),
          });
        }
      },
      error: () => this.myProfile.set(null),
    });
  }

  /** The photo path for an author: the reader's own, or what their lookup returned. */
  photoOf(authorMemberId: string): string | null {
    if (authorMemberId === this.me()) {
      return this.myPhoto();
    }

    return this.photos()[authorMemberId] ?? null;
  }

  /** "Ravi Shah" -> "RS"; a fallback label like "A member" -> "A". */
  initials(name: string): string {
    const parts = name.trim().split(/\s+/).filter((part) => part.length > 0);
    const first = parts[0]?.[0] ?? '';
    const last = parts.length > 1 ? (parts[parts.length - 1]?.[0] ?? '') : '';

    return (first + last).toUpperCase() || '?';
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.moduleDisabled.set(false);

    this.api.feed().subscribe({
      next: (found) => {
        this.posts.set(found);
        this.loading.set(false);
        this.resolveNames(found.map((post) => post.authorMemberId));
      },
      error: (failure: unknown) => {
        this.loading.set(false);

        if (failure instanceof HttpErrorResponse && failure.status === 404) {
          this.moduleDisabled.set(true);
          return;
        }

        this.error.set(describeError(failure));
      },
    });
  }

  // ---- Composing --------------------------------------------------------

  submit(): void {
    if (!this.canPost() || this.posting()) {
      return;
    }

    this.posting.set(true);
    this.composeError.set(null);
    this.justPosted.set(false);

    this.api.create(this.title.trim(), this.body.trim(), this.asAnnouncement).subscribe({
      next: (created) => {
        // Prepended rather than re-fetched: the member should see their own
        // post immediately, and the feed is newest-first anyway.
        this.posts.set([created, ...this.posts()]);
        this.title = '';
        this.body = '';
        this.justPosted.set(true);
        this.posting.set(false);
      },
      error: (failure: unknown) => {
        this.composeError.set(describeError(failure));
        this.posting.set(false);
      },
    });
  }

  // ---- Reacting, commenting, reporting ----------------------------------

  react(post: Post, reaction: ReactionType): void {
    this.busyId.set(post.id);
    this.clearErrorFor(post.id);

    this.api.react(post.id, reaction).subscribe({
      next: (updated) => {
        this.replace(updated);
        this.busyId.set(null);
      },
      error: (failure: unknown) => {
        this.setErrorFor(post.id, describeError(failure));
        this.busyId.set(null);
      },
    });
  }

  toggleComments(post: Post): void {
    if (this.openComments() === post.id) {
      this.openComments.set(null);
      return;
    }

    this.openComments.set(post.id);
    this.comments.set([]);
    this.draftComment = '';
    this.loadingComments.set(true);
    this.clearErrorFor(post.id);

    this.api.post(post.id).subscribe({
      next: (detail) => {
        this.comments.set(detail.comments);
        this.resolveNames(detail.comments.map((comment) => comment.authorMemberId));

        // The detail carries a fresher copy of the post than the feed does.
        this.replace(detail.post);
        this.loadingComments.set(false);
      },
      error: (failure: unknown) => {
        this.setErrorFor(post.id, describeError(failure));
        this.loadingComments.set(false);
        this.openComments.set(null);
      },
    });
  }

  addComment(post: Post): void {
    const body = this.draftComment.trim();

    if (body.length === 0) {
      return;
    }

    this.clearErrorFor(post.id);

    this.api.comment(post.id, body).subscribe({
      next: (added) => {
        this.comments.set([...this.comments(), added]);
        this.draftComment = '';

        // The count lives on the post, so it is bumped here rather than by
        // re-fetching the whole feed for one number.
        this.replace({ ...post, commentCount: post.commentCount + 1 });
      },
      error: (failure: unknown) => this.setErrorFor(post.id, describeError(failure)),
    });
  }

  report(post: Post): void {
    this.busyId.set(post.id);
    this.clearErrorFor(post.id);

    this.api.report(post.id).subscribe({
      next: () => {
        // Nothing about the post changes: reporting flags it for a moderator
        // and removes nothing. The button says "Reported" so the member knows
        // it landed and does not press it again.
        this.reported.set([...this.reported(), post.id]);
        this.busyId.set(null);
      },
      error: (failure: unknown) => {
        this.setErrorFor(post.id, describeError(failure));
        this.busyId.set(null);
      },
    });
  }

  // ---- Rendering --------------------------------------------------------

  isMine(post: Post): boolean {
    return this.me() !== null && post.authorMemberId === this.me();
  }

  /** A member's own post, still with the moderators. */
  isAwaitingReview(post: Post): boolean {
    return post.status === 'PendingReview';
  }

  /**
   * Whether the post is one the Samaaj can see, and so one worth reacting to.
   *
   * Reacting to or reporting a post nobody else can see is meaningless, and
   * offering it would suggest the post is public when it is not.
   */
  isReadable(post: Post): boolean {
    return post.status === 'Approved';
  }

  /** The wireframe's three meta lines, decided by the data rather than fixed. */
  label(post: Post): string {
    if (this.isMine(post)) {
      return this.isAwaitingReview(post) ? 'Your post • Awaiting review' : 'Your post';
    }

    return post.type === 'Announcement' ? 'Samaaj announcement' : 'Member post';
  }

  /**
   * What to call an author. The reader is named "You"; everyone else's real
   * name comes from `resolveNames()` once it has loaded, falling back to a
   * generic label until then or if that lookup failed.
   */
  authorLabel(authorMemberId: string): string {
    if (authorMemberId === this.me()) {
      return 'You';
    }

    return this.names()[authorMemberId] ?? 'A member';
  }

  countOf(post: Post, type: string): number {
    return post.reactions.find((reaction) => reaction.type === type)?.count ?? 0;
  }

  /**
   * A short relative time, as the wireframe's "2h ago".
   *
   * Formatted here rather than by a pipe because it is the only place in the
   * app that needs it; it moves to `libs/shared` when a second screen does.
   */
  when(iso: string): string {
    const then = new Date(iso).getTime();

    if (Number.isNaN(then)) {
      return '';
    }

    const minutes = Math.floor((Date.now() - then) / 60000);

    if (minutes < 1) {
      return 'just now';
    }

    if (minutes < 60) {
      return `${minutes}m ago`;
    }

    const hours = Math.floor(minutes / 60);

    if (hours < 24) {
      return `${hours}h ago`;
    }

    const days = Math.floor(hours / 24);

    return days < 7 ? `${days}d ago` : new Date(iso).toLocaleDateString();
  }

  /**
   * Resolves authors' names, one call per not-yet-resolved id actually on
   * screen - the loaded page of the feed, or the open comment thread - never
   * the whole Samaaj directory. A failed lookup is left unresolved rather
   * than raised as a page-level error; `authorLabel` already falls back for
   * that.
   */
  private resolveNames(authorMemberIds: readonly string[]): void {
    const known = this.names();
    const me = this.me();

    const toResolve = [...new Set(authorMemberIds)].filter((id) => id !== me && !(id in known));

    for (const id of toResolve) {
      this.members.get(id).subscribe({
        next: (member) => {
          this.names.update((current) => ({ ...current, [id]: member.fullName }));
          this.photos.update((current) => ({ ...current, [id]: member.photoUrl }));
        },
        error: () => {
          // Left unresolved; authorLabel() falls back to a generic label.
        },
      });
    }
  }

  private replace(updated: Post): void {
    this.posts.set(this.posts().map((post) => (post.id === updated.id ? updated : post)));
  }

  private setErrorFor(id: string, message: string): void {
    this.postError.set({ ...this.postError(), [id]: message });
  }

  private clearErrorFor(id: string): void {
    const { [id]: _removed, ...rest } = this.postError();

    this.postError.set(rest);
  }
}
