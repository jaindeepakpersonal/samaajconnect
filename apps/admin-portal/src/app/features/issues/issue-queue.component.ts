import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { IssueReviewAccess, IssueStatus, SocialIssue } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';

/**
 * Social issues — what the Samaaj Panch has to decide about.
 *
 * Wireframe `#issues`.
 *
 * **Reviewing moved to the Panch, and this screen followed it.** An issue used
 * to be an administrator's to approve, reject, or send back — this queue and
 * `SocialIssues.Approve`. Once a Samaaj elects a Panch (panch-service), only
 * its members review; an administrator keeps the queue so somebody can tell
 * whether it is being worked, but loses every button. **Before** a Panch is
 * seated, an administrator still reviews — the fallback that keeps every
 * Samaaj's queue moving until it elects one.
 *
 * **The buttons come from the server, and nothing here derives them.**
 * `availableTransitions` is the domain's transition table filtered by this
 * caller's permission — empty for an administrator once a Panch exists, which
 * is what actually turns this screen read-only rather than a client-side
 * guess at whose turn it is.
 *
 * **A reason is asked for on the refusing moves.** The service requires one
 * where a member will ask why — rejecting, or sending it back for changes — and
 * ignores it elsewhere, so the screen asks exactly there and always sends the
 * field.
 *
 * **Recording the Panch's decision is not here.** `RecordDecisionCommand`
 * needs a decision text and belongs to whoever sits on the Panch — an ordinary
 * member, not necessarily an administrator — so that form lives in
 * member-portal. This screen shows the decision once it exists, read-only.
 */
@Component({
  selector: 'app-issue-queue',
  imports: [FormsModule, DatePipe],
  template: `
    <h1 class="title">Social issues</h1>
    <p class="sub">What the Samaaj Panch has to decide about, oldest first.</p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (moduleOff()) {
      <p class="notice">
        {{ scope.label() }} does not run the social issues module. Switch it on under the
        Samaaj's settings.
      </p>
    } @else if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else {
      @if (access(); as granted) {
        @if (granted.panelSeated && !granted.canReview) {
          <p class="notice">
            {{ scope.label() }} has elected a Samaaj Panch. Only its members can act on issues
            now — this queue is read-only here.
          </p>
        }
      }

      @if (issues().length === 0) {
        <p class="empty">
          Nothing waiting. Issues appear here when a member submits one.
        </p>
      } @else {
        @for (issue of issues(); track issue.id) {
          <div class="card spaced">
            <h2>{{ issue.title }}</h2>
            <p class="sub">
              {{ issue.category }}
              @if (issue.locality) { · {{ issue.locality }} }
              · {{ issue.status }}
              · raised {{ issue.createdAt | date: 'd MMM y' }}
            </p>

            <p>{{ issue.description }}</p>

            @if (issue.decision; as decision) {
              <div class="notice ok">
                <b>The Panch's decision:</b> {{ decision }}
                @if (issue.decidedAt) {
                  <span class="small"> — {{ issue.decidedAt | date: 'd MMM y' }}</span>
                }
              </div>
            } @else if (issue.availableTransitions.length === 0) {
              <p class="empty">Nothing for you to decide on this one.</p>
            } @else {
              @if (needsReason(issue)) {
                <label [attr.for]="'reason-' + issue.id">Reason</label>
                <input
                  class="input"
                  [id]="'reason-' + issue.id"
                  [name]="'reason-' + issue.id"
                  [(ngModel)]="reason[issue.id]"
                  maxlength="1000"
                  placeholder="Why is it going back, or being turned down?"
                />
                <p class="small">
                  Required for the moves a member will ask about. They are shown it.
                </p>
              }

              <div class="row-actions">
                @for (next of issue.availableTransitions; track next) {
                  <button
                    class="btn small"
                    type="button"
                    [class.alt]="refusing(next)"
                    [disabled]="busy() || (refusing(next) && !(reason[issue.id] ?? '').trim())"
                    (click)="move(issue, next)"
                  >
                    {{ label(next) }}
                  </button>
                }
              </div>
            }
          </div>
        }
      }
    }
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
      margin-top: var(--space-3);
    }
  `,
})
export class IssueQueueComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly scope = inject(AdminScope);

  readonly issues = signal<readonly SocialIssue[]>([]);
  readonly access = signal<IssueReviewAccess | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly moduleOff = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  /**
   * Issue id → the reason typed for it, if any. Genuinely `undefined` for an
   * issue nobody has typed into yet — `[(ngModel)]` reads before it ever
   * writes, and `Record<string, string>` claims otherwise only because
   * TypeScript does not check index signatures against reality by default.
   * The `?? ''` fallback at every read is load-bearing, not dead code a
   * stricter type would prove safe to delete.
   */
  reason: Record<string, string | undefined> = {};

  ngOnInit(): void {
    this.load();
  }

  /**
   * The moves a member will want an explanation for.
   *
   * Kept as a list rather than "anything that is not an approval", because the
   * set the service requires a reason for is its decision and not something to
   * be inferred from a name.
   */
  refusing(status: IssueStatus): boolean {
    return status === 'Rejected' || status === 'ChangesRequested';
  }

  /** Whether any of this issue's available moves would need a reason. */
  needsReason(issue: SocialIssue): boolean {
    return issue.availableTransitions.some((t) => this.refusing(t));
  }

  label(status: IssueStatus): string {
    switch (status) {
      case 'UnderReview':
        return 'Pick it up';
      case 'DecisionPending':
        return 'Put in front of the Panch';
      case 'Rejected':
        return 'Reject';
      case 'ChangesRequested':
        return 'Send back for changes';
      case 'Closed':
        return 'Close';
      default:
        return status;
    }
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.issueReviewAccess().subscribe({
      next: (found) => this.access.set(found),
      error: () => this.access.set(null),
    });

    this.api.issueApprovalQueue().subscribe({
      next: (found) => {
        this.issues.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        if (isNotFound(failure)) {
          this.moduleOff.set(true);
        } else {
          this.error.set(describeError(failure));
        }

        this.loading.set(false);
      },
    });
  }

  move(issue: SocialIssue, status: IssueStatus): void {
    const reason = (this.reason[issue.id] ?? '').trim();

    if (this.refusing(status) && reason.length === 0) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    // The reason is always sent and the service ignores it where it does not
    // apply — deciding here which moves carry one would be a second copy of a
    // rule that belongs to the service.
    this.api.moveIssue(issue.id, status, reason.length > 0 ? reason : null).subscribe({
      next: () => {
        this.done.set(`"${issue.title}" is now ${status}.`);
        this.busy.set(false);
        delete this.reason[issue.id];
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}
