import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { ModerationDecision, ModerationQueueEntry } from '../../core/admin.models';

/**
 * Timeline / Content Moderation, from the admin wireframe's `#content` screen.
 *
 * **Nothing on the platform could approve a post before this.** A member writes
 * one, `TimelinePost.Create` puts it in `PendingReview`, and the only way it
 * ever reached the Samaaj's timeline was somebody curling
 * `POST /v1/timeline/posts/{id}/moderate`. The queue endpoint and the moderate
 * endpoint both existed; no screen in either app called either of them.
 *
 * **The buttons come from the server.** Each row carries `availableDecisions`
 * from `TimelinePost.AvailableDecisions`, and this screen renders exactly those.
 * Deriving them from the status would put a second copy of the rule here — the
 * same mistake the social-issues screen in the member portal exists not to make.
 *
 * **Rejecting and hiding ask for a reason before they will send.** The service
 * requires one for both, because those are the cases where the member will ask
 * why. Approving does not, and the box is not shown for it.
 *
 * The wireframe's table also lists an already-approved post with a "View"
 * button. The queue deliberately holds only what needs a decision — pending
 * posts and approved ones members have reported — so browsing the whole timeline
 * is not here; the member portal shows the timeline itself.
 */
@Component({
  selector: 'app-moderation-queue',
  imports: [FormsModule, DatePipe],
  template: `
    <h1 class="title">Timeline / Content Moderation</h1>
    <p class="sub">Posts awaiting review in {{ scope.label() }}, and ones members have reported.</p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (needsSamaaj()) {
      <p class="notice">
        Posts belong to a Samaaj. Choose one in the top bar to see its queue.
      </p>
    } @else if (moduleOff()) {
      <p class="notice">
        {{ scope.label() }} does not run the community module, so it has no timeline to
        moderate. Switch it on from the Samaaj screen.
      </p>
    } @else if (loading()) {
      <p class="empty" role="status">Loading the queue…</p>
    } @else if (queue().length === 0) {
      <p class="empty">Nothing is waiting. Reported posts appear here too.</p>
    } @else {
      @for (entry of queue(); track entry.post.id) {
        <div class="card post">
          <div class="post-head">
            <div>
              <h3>{{ entry.post.title }}</h3>
              <p class="muted">
                {{ authorName(entry.post.authorMemberId) }}
                · {{ entry.post.createdAt | date: 'd MMM y, HH:mm' }}
                · {{ entry.post.commentCount }}
                {{ entry.post.commentCount === 1 ? 'comment' : 'comments' }}
              </p>
            </div>
            <div class="pills">
              @if (entry.post.status === 'PendingReview') {
                <span class="pill warn">Awaiting review</span>
              } @else {
                <span class="pill">{{ entry.post.status }}</span>
              }
              @if (entry.post.reportCount > 0) {
                <span class="pill danger">
                  Reported {{ entry.post.reportCount }}
                  {{ entry.post.reportCount === 1 ? 'time' : 'times' }}
                </span>
              }
            </div>
          </div>

          <p class="post-body">{{ entry.post.body }}</p>

          @if (entry.history.length > 0) {
            <details class="history">
              <summary>
                Moderation history ({{ entry.history.length }})
              </summary>
              <ul>
                @for (action of entry.history; track action.createdAt) {
                  <li>
                    <b>{{ action.action }}</b>
                    · {{ action.createdAt | date: 'd MMM y, HH:mm' }}
                    @if (action.reason) {
                      — {{ action.reason }}
                    }
                  </li>
                }
              </ul>
            </details>
          }

          @if (needsReason(entry)) {
            <label [attr.for]="'reason-' + entry.post.id">
              Reason, shown to the member
            </label>
            <input
              class="input"
              [id]="'reason-' + entry.post.id"
              [name]="'reason-' + entry.post.id"
              [(ngModel)]="reasons[entry.post.id]"
              maxlength="1000"
              placeholder="Why this is not going on the timeline"
              [attr.aria-invalid]="missingReason() === entry.post.id ? 'true' : null"
            />
            @if (missingReason() === entry.post.id) {
              <p class="notice error" role="alert">
                Say why. The member is told this, and "no reason given" is not an answer.
              </p>
            }
          }

          <div class="actions">
            @for (decision of entry.availableDecisions; track decision) {
              <button
                class="btn"
                [class.alt]="decision !== 'Approve'"
                type="button"
                [disabled]="busy() !== null"
                (click)="decide(entry, decision)"
              >
                {{ label(decision) }}
              </button>
            }
          </div>
        </div>
      }
    }
  `,
  styles: `
    .post {
      margin-bottom: var(--space-4);
    }

    .post-head {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .post-head h3 {
      margin: 0;
    }

    .pills {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
    }

    /* Keeps the line breaks a member typed without letting a long word widen
       the card past the page. */
    .post-body {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      margin: var(--space-3) 0;
    }

    .history {
      margin-bottom: var(--space-3);
      font-size: 12px;
      color: var(--muted);
    }

    .history ul {
      margin: var(--space-2) 0 0;
      padding-left: var(--space-4);
    }
  `,
})
export class ModerationQueueComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);

  readonly scope = inject(AdminScope);

  readonly queue = signal<readonly ModerationQueueEntry[]>([]);
  readonly loading = signal(true);
  readonly moduleOff = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  /** The post id currently being decided, so one click does not fire twice. */
  readonly busy = signal<string | null>(null);

  /** The post id whose reason box is empty when it must not be. */
  readonly missingReason = signal<string | null>(null);

  readonly needsSamaaj = computed(
    () => this.auth.roles().includes('SuperAdmin') && this.scope.tenantId() === null,
  );

  /** Author id → name, from one directory call rather than one per row. */
  private readonly names = signal<ReadonlyMap<string, string>>(new Map());

  reasons: Record<string, string> = {};

  ngOnInit(): void {
    if (this.needsSamaaj()) {
      this.loading.set(false);
      return;
    }

    this.loadNames();
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.moderationQueue().subscribe({
      next: (entries) => {
        this.queue.set(entries);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        // A 404 here is the module gate, not a missing endpoint: the gateway
        // answers 404 for a Samaaj that has switched `community` off, so that
        // a Samaaj without the module is indistinguishable from a platform
        // that has no such feature. Reporting it as an error would send an
        // administrator looking for a bug.
        if (isNotFound(failure)) {
          this.moduleOff.set(true);
        } else {
          this.error.set(describeError(failure));
        }

        this.loading.set(false);
      },
    });
  }

  /**
   * Names are a convenience, so a failure here is silent. A moderator can still
   * decide about a post whose author shows as an id; they cannot decide about
   * one they never see.
   */
  private loadNames(): void {
    this.api.listMembers().subscribe({
      next: (members) => this.names.set(new Map(members.map((m) => [m.id, m.fullName]))),
      error: () => this.names.set(new Map()),
    });
  }

  authorName(memberId: string): string {
    return this.names().get(memberId) ?? 'A member';
  }

  /** The two decisions the service will refuse without a reason. */
  needsReason(entry: ModerationQueueEntry): boolean {
    return entry.availableDecisions.some((d) => d === 'Reject' || d === 'Hide');
  }

  label(decision: ModerationDecision): string {
    switch (decision) {
      case 'Approve':
        return 'Approve';
      case 'Reject':
        return 'Reject';
      case 'Hide':
        return 'Take down';
      default:
        return 'Restore';
    }
  }

  /**
   * Written out rather than derived from the button label. Adding "d" to
   * "Take down" gives "take downd", which is exactly the kind of thing that
   * ships because nobody clicks the third button.
   */
  private static readonly Confirmations: Record<ModerationDecision, string> = {
    Approve: 'is now on the timeline',
    Reject: 'was rejected',
    Hide: 'has been taken down',
    Restore: 'is back on the timeline',
  };

  decide(entry: ModerationQueueEntry, decision: ModerationDecision): void {
    const reason = (this.reasons[entry.post.id] ?? '').trim();
    const explain = decision === 'Reject' || decision === 'Hide';

    // Checked here as well as by the service, so a moderator is told before the
    // round trip rather than by a 400 that scrolls past.
    if (explain && reason.length === 0) {
      this.missingReason.set(entry.post.id);
      return;
    }

    this.missingReason.set(null);
    this.error.set(null);
    this.done.set(null);
    this.busy.set(entry.post.id);

    this.api.moderatePost(entry.post.id, decision, explain ? reason : null).subscribe({
      next: () => {
        this.done.set(
          `"${entry.post.title}" ${ModerationQueueComponent.Confirmations[decision]}.`,
        );
        this.reasons[entry.post.id] = '';
        this.busy.set(null);

        // Re-read rather than dropping the row locally. A decision can leave a
        // post in the queue — hiding a reported post takes it out, approving a
        // reported one does not — and the server is the only thing that knows.
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(null);
      },
    });
  }
}

function isNotFound(failure: unknown): boolean {
  return typeof failure === 'object' && failure !== null && 'status' in failure
    && (failure as { status: unknown }).status === 404;
}
