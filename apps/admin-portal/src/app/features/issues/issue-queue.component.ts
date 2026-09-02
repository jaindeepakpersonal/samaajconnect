import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { IssueStatus, SocialIssue } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';

/**
 * Social issues — what a reviewer has to decide about.
 *
 * Wireframe `#issues`.
 *
 * **The mildest gap of the set, and worth saying so.** Unlike the others, a
 * reviewer was not stuck: the member portal's issue detail already renders
 * `availableTransitions`, so somebody who had an issue's id could decide it.
 * What was missing was the list — a reviewer had no way to find out that
 * anything was waiting, which makes the queue a thing somebody has to be told
 * about out of band.
 *
 * **The buttons come from the server, and nothing here derives them.**
 * `availableTransitions` is the domain's transition table filtered by this
 * caller's permission. social-issues-service is the one service on the platform
 * whose workflow is a real table with branches rather than a short line, so a
 * second copy of it in the panel would be the copy that drifts. This is the
 * same rule the moderation queue follows.
 *
 * **A reason is asked for on the refusing moves.** The service requires one
 * where a member will ask why — rejecting, or sending it back for changes — and
 * ignores it elsewhere, so the screen asks exactly there and always sends the
 * field.
 */
@Component({
  selector: 'app-issue-queue',
  imports: [FormsModule, DatePipe],
  template: `
    <h1 class="title">Social issues</h1>
    <p class="sub">The approval queue: what a reviewer has to decide about, oldest first.</p>

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
    } @else if (issues().length === 0) {
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

          @if (issue.availableTransitions.length === 0) {
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
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly moduleOff = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  /** Issue id → the reason typed for it, if any. */
  reason: Record<string, string> = {};

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
      case 'Approved':
        return 'Approve';
      case 'Published':
        return 'Publish';
      case 'Rejected':
        return 'Reject';
      case 'ChangesRequested':
        return 'Send back for changes';
      case 'Resolved':
        return 'Mark resolved';
      default:
        return status;
    }
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

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
