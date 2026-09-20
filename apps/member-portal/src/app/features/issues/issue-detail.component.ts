import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { GroupsApi } from '../groups/groups.api';
import { VolunteerGroup } from '../groups/groups.models';
import { IssuesApi } from './issues.api';
import {
  IssueCategories,
  IssueDetail,
  IssueStatus,
  StatusLabels,
  TransitionLabels,
  TransitionsNeedingReason,
} from './issues.models';

/**
 * One social issue, its history, and whatever this caller may do to it next.
 *
 * No wireframe covers this screen, and it is where the workflow actually
 * becomes usable. The list can say "Not accepted"; only here can it say why -
 * and "why was mine sent back?" is the question the service built an
 * append-only history to answer.
 *
 * **Every button comes from `availableTransitions`.** The service computes that
 * from the same transition table the aggregate enforces, so a button that
 * appears is a move the server will accept. Deriving the buttons from the
 * status here would put a second copy of an eight-state table in the portal,
 * and the two would drift - the first time somebody added a state, the screen
 * would be confidently wrong.
 *
 * That also means this one screen serves a member and a reviewer without
 * knowing which it is talking to. A reviewer sees Reject, Ask for changes and
 * Put in front of the Panch because the service put them in the list; an
 * author sees Submit or Close for the same reason.
 *
 * **The Panch's decision is a separate form, not a transition button.**
 * `RecordDecisionCommand` needs a decision text and an optional volunteer
 * group, so it cannot be one more entry in `availableTransitions` the way a
 * plain status move is. The service says whether this form belongs on screen
 * with `issue.canDecide`, which is the same "a seated Panch member, or an
 * administrator while no Panch is seated" rule the queue is gated on - never
 * derived here from role or status alone.
 */
@Component({
  selector: 'app-issue-detail',
  imports: [FormsModule, RouterLink],
  styleUrl: './issues.css',
  template: `
    <div class="issues-page">
      <a class="back" routerLink="/issues">‹ Back to Social Issues</a>

      @if (loading()) {
        <p role="status">Loading the issue…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (detail(); as found) {
        <h1 class="page-title">{{ found.issue.title }}</h1>
        <p class="subtitle">
          <span class="pill" [class]="pillClass(found.issue.status)">
            {{ label(found.issue.status) }}
          </span>
          {{ found.issue.category }}
          @if (found.issue.locality; as locality) {
            • {{ locality }}
          }
          @if (found.issue.isMine) {
            • Raised by you
          }
        </p>

        @if (latestReason(); as reason) {
          <!-- The whole point of the history: the author is owed the reason,
               and it should not take opening a timeline to find it. -->
          <div class="notice" [class.error]="found.issue.status === 'Rejected'"
            [class.info]="found.issue.status !== 'Rejected'" role="status">
            <strong>{{ reasonHeading(found.issue.status) }}</strong>
            {{ reason }}
          </div>
        }

        <div class="grid2">
          <!-- The issue ------------------------------------------------- -->
          <div class="card">
            <h2>The issue</h2>

            @if (editing()) {
              <form (ngSubmit)="saveEdit()">
                <div class="form-grid">
                  <div class="field">
                    <label for="edit-title">Title</label>
                    <input
                      class="input"
                      id="edit-title"
                      name="title"
                      [(ngModel)]="title"
                      maxlength="200"
                      required
                    />
                  </div>

                  <div class="field">
                    <label for="edit-category">Category</label>
                    <select class="input" id="edit-category" name="category" [(ngModel)]="category">
                      @for (option of categories; track option) {
                        <option [value]="option">{{ option }}</option>
                      }
                    </select>
                  </div>

                  <div class="field">
                    <label for="edit-locality">Locality (optional)</label>
                    <input
                      class="input"
                      id="edit-locality"
                      name="locality"
                      [(ngModel)]="locality"
                      maxlength="150"
                    />
                  </div>

                  <div class="field span-all">
                    <label for="edit-description">Description</label>
                    <textarea
                      class="input"
                      id="edit-description"
                      name="description"
                      [(ngModel)]="description"
                      rows="5"
                      maxlength="5000"
                      required
                    ></textarea>
                  </div>

                  <div class="actions span-all">
                    <button class="btn" type="submit" [disabled]="busy()">Save changes</button>
                    <button class="btn secondary" type="button" (click)="cancelEdit()">
                      Cancel
                    </button>
                  </div>
                </div>
              </form>
            } @else {
              <p class="issue-body">{{ found.issue.description }}</p>

              @if (found.issue.isMine) {
                <div class="actions">
                  <button class="btn secondary" type="button" (click)="startEdit(found)">
                    Edit
                  </button>
                </div>
              }
            }
          </div>

          <!-- What happens next ----------------------------------------- -->
          <div class="card">
            <h2>What happens next</h2>

            @if (found.issue.availableTransitions.length === 0) {
              <p class="small">
                @if (found.issue.canDecide) {
                  Record the Panch's decision below.
                } @else {
                  There is nothing for you to do here. {{ describe(found.issue.status) }}
                }
              </p>
            } @else {
              @if (actionError(); as message) {
                <p class="notice error" role="alert">{{ message }}</p>
              }

              @if (needsReason(pendingMove())) {
                <label for="move-reason">
                  Reason ({{ transitionLabel(pendingMove()!) }} needs one)
                </label>
                <textarea
                  class="input"
                  id="move-reason"
                  name="reason"
                  [(ngModel)]="reason"
                  rows="3"
                  maxlength="1000"
                  placeholder="What the author needs to know."
                ></textarea>

                <div class="actions">
                  <button
                    class="btn"
                    type="button"
                    [disabled]="busy() || reason.trim().length === 0"
                    (click)="confirmMove()"
                  >
                    {{ busy() ? 'Working…' : transitionLabel(pendingMove()!) }}
                  </button>
                  <button class="btn secondary" type="button" (click)="pendingMove.set(null)">
                    Cancel
                  </button>
                </div>
              } @else {
                <div class="actions">
                  @for (target of found.issue.availableTransitions; track target) {
                    <button
                      class="btn"
                      [class.secondary]="target === 'Rejected' || target === 'Closed'"
                      type="button"
                      [disabled]="busy()"
                      (click)="move(target)"
                    >
                      {{ transitionLabel(target) }}
                    </button>
                  }
                </div>
              }
            }
          </div>
        </div>

        <!-- The Panch's decision ---------------------------------------- -->
        @if (found.issue.decision; as decision) {
          <div class="card spaced">
            <h2>The Panch's decision</h2>
            <p class="issue-body">{{ decision }}</p>
            @if (found.issue.monitoringGroupId; as groupId) {
              <p class="small">
                Monitoring handed to <b>{{ groupName(groupId) }}</b>.
              </p>
            }
            @if (found.issue.decidedAt; as decidedAt) {
              <p class="small">Decided {{ when(decidedAt) }}.</p>
            }
          </div>
        } @else if (found.issue.canDecide) {
          <div class="card spaced">
            <h2>Record the Panch's decision</h2>
            <p class="small">
              Say what the Panch decided. Hand the monitoring to a volunteer group if one is
              needed - this closes the issue.
            </p>

            <form (ngSubmit)="decide()">
              <div class="form-grid">
                <div class="field span-all">
                  <label for="decision-text">Decision</label>
                  <textarea
                    class="input"
                    id="decision-text"
                    name="decision"
                    [(ngModel)]="decisionText"
                    rows="4"
                    maxlength="2000"
                    required
                  ></textarea>
                </div>

                <div class="field">
                  <label for="decision-group">Hand monitoring to (optional)</label>
                  <select
                    class="input"
                    id="decision-group"
                    name="monitoringGroup"
                    [(ngModel)]="monitoringGroupId"
                  >
                    <option value="">No group</option>
                    @for (group of groups(); track group.id) {
                      <option [value]="group.id">{{ group.name }}</option>
                    }
                  </select>
                </div>

                @if (decisionError(); as message) {
                  <p class="notice error span-all" role="alert">{{ message }}</p>
                }

                <div class="actions span-all">
                  <button
                    class="btn"
                    type="submit"
                    [disabled]="busy() || decisionText.trim().length === 0"
                  >
                    {{ busy() ? 'Recording…' : 'Record decision and close' }}
                  </button>
                </div>
              </div>
            </form>
          </div>
        }

        <!-- History ----------------------------------------------------- -->
        <h2 class="section-heading">History</h2>

        @if (found.history.length === 0) {
          <p class="small">Nothing has happened to it yet.</p>
        } @else {
          <ol class="history">
            @for (entry of found.history; track $index) {
              <li>
                <div class="meta">
                  <span>
                    @if (entry.fromStatus; as from) {
                      {{ label(from) }} → {{ label(entry.toStatus) }}
                    } @else {
                      Raised as {{ label(entry.toStatus) }}
                    }
                  </span>
                  <span>{{ when(entry.createdAt) }}</span>
                  <span>{{ actor(entry.actorUserId) }}</span>
                </div>

                @if (entry.reason; as reason) {
                  <p class="issue-body">{{ reason }}</p>
                }
              </li>
            }
          </ol>
        }
      }
    </div>
  `,
})
export class IssueDetailComponent implements OnInit {
  private readonly api = inject(IssuesApi);
  private readonly groupsApi = inject(GroupsApi);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  readonly categories = IssueCategories;

  readonly detail = signal<IssueDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly busy = signal(false);
  readonly actionError = signal<string | null>(null);
  readonly editing = signal(false);

  /** The move waiting on a reason, if the one chosen needs one. */
  readonly pendingMove = signal<IssueStatus | null>(null);

  /** Loaded only once, and only for somebody who might decide - see `load()`. */
  readonly groups = signal<readonly VolunteerGroup[]>([]);
  readonly decisionError = signal<string | null>(null);

  title = '';
  description = '';
  category: string = IssueCategories[0];
  locality = '';
  reason = '';
  decisionText = '';
  monitoringGroupId = '';

  private readonly me = computed(() => this.auth.user()?.userId ?? null);

  /**
   * The reason on the most recent step that carried one.
   *
   * Surfaced at the top rather than left in the timeline: an author opening a
   * rejected issue is here for exactly this, and making them read a history to
   * find it is making them work for an answer they are owed.
   */
  readonly latestReason = computed(() => {
    const history = this.detail()?.history ?? [];

    for (const step of [...history].reverse()) {
      if (step.reason !== null && step.reason.length > 0) {
        return step.reason;
      }
    }

    return null;
  });

  ngOnInit(): void {
    // The history names actors by id and the screen labels the reader as
    // "You", so it waits for /me before rendering.
    this.auth.ensureCurrentUser().subscribe({
      next: () => this.load(),
      error: () => this.load(),
    });
  }

  load(): void {
    const id = this.route.snapshot.paramMap.get('id');

    if (id === null) {
      this.loading.set(false);
      this.error.set('That link does not name an issue.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.api.get(id).subscribe({
      next: (found) => {
        this.detail.set(found);
        this.loading.set(false);

        // Needed to fill the picker, or to resolve a name already recorded.
        if (found.issue.canDecide || found.issue.monitoringGroupId !== null) {
          this.loadGroups();
        }
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  /** For the monitoring-group picker. Only fetched when a decision might be made here. */
  private loadGroups(): void {
    this.groupsApi.list().subscribe({
      next: (found) => this.groups.set(found),
      error: () => this.groups.set([]),
    });
  }

  // ---- Moving it on ------------------------------------------------------

  needsReason(target: IssueStatus | null): boolean {
    return target !== null && TransitionsNeedingReason.includes(target);
  }

  /**
   * Starts a move. One that needs a reason asks for it first rather than
   * sending a request the server will refuse.
   */
  move(target: IssueStatus): void {
    this.actionError.set(null);

    if (this.needsReason(target)) {
      this.reason = '';
      this.pendingMove.set(target);
      return;
    }

    this.send(target, null);
  }

  confirmMove(): void {
    const target = this.pendingMove();

    if (target === null || this.reason.trim().length === 0) {
      return;
    }

    this.send(target, this.reason.trim());
  }

  private send(target: IssueStatus, reason: string | null): void {
    const found = this.detail();

    if (found === null) {
      return;
    }

    this.busy.set(true);

    this.api.move(found.issue.id, target, reason).subscribe({
      next: () => {
        this.pendingMove.set(null);
        this.reason = '';
        this.busy.set(false);

        // Re-read rather than patch: the move added a history entry and
        // changed which transitions are now legal.
        this.load();
      },
      error: (failure: unknown) => {
        this.actionError.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  // ---- The Panch's decision -----------------------------------------------

  decide(): void {
    const found = this.detail();

    if (found === null || this.decisionText.trim().length === 0) {
      return;
    }

    this.busy.set(true);
    this.decisionError.set(null);

    this.api
      .decide(
        found.issue.id,
        this.decisionText.trim(),
        this.monitoringGroupId.length > 0 ? this.monitoringGroupId : null,
      )
      .subscribe({
        next: () => {
          this.decisionText = '';
          this.monitoringGroupId = '';
          this.busy.set(false);
          this.load();
        },
        error: (failure: unknown) => {
          this.decisionError.set(describeError(failure));
          this.busy.set(false);
        },
      });
  }

  /** A group's name, resolved from the same list the picker offers. Falls back to "a group". */
  groupName(groupId: string): string {
    return this.groups().find((group) => group.id === groupId)?.name ?? 'a group';
  }

  // ---- Correcting it -----------------------------------------------------

  startEdit(detail: IssueDetail): void {
    this.title = detail.issue.title;
    this.description = detail.issue.description;
    this.category = detail.issue.category;
    this.locality = detail.issue.locality ?? '';
    this.editing.set(true);
  }

  cancelEdit(): void {
    this.editing.set(false);
  }

  saveEdit(): void {
    const found = this.detail();

    if (found === null || this.title.trim().length === 0) {
      return;
    }

    this.busy.set(true);
    this.actionError.set(null);

    const locality = this.locality.trim();

    this.api
      .revise(
        found.issue.id,
        this.title.trim(),
        this.description.trim(),
        this.category,
        locality.length > 0 ? locality : null,
      )
      .subscribe({
        next: () => {
          this.editing.set(false);
          this.busy.set(false);
          this.load();
        },
        error: (failure: unknown) => {
          // Kept on screen with the form still open, so a refusal does not
          // cost what was typed.
          this.actionError.set(describeError(failure));
          this.busy.set(false);
        },
      });
  }

  // ---- Rendering ---------------------------------------------------------

  label(status: IssueStatus): string {
    return StatusLabels[status];
  }

  transitionLabel(target: IssueStatus): string {
    return TransitionLabels[target];
  }

  pillClass(status: IssueStatus): string {
    if (status === 'Rejected') {
      return 'danger';
    }

    if (status === 'ChangesRequested') {
      return 'warn';
    }

    return status === 'DecisionPending' || status === 'Closed' ? 'ok' : '';
  }

  reasonHeading(status: IssueStatus): string {
    if (status === 'Rejected') {
      return 'Why this was not accepted:';
    }

    return status === 'ChangesRequested' ? 'What was asked for:' : 'Note from the reviewer:';
  }

  describe(status: IssueStatus): string {
    switch (status) {
      case 'DecisionPending':
        return 'It is in front of the Samaaj Panch, awaiting its decision.';
      case 'Closed':
        return 'It has been closed.';
      case 'Rejected':
        return 'It was not accepted.';
      default:
        return 'Somebody else has the next move.';
    }
  }

  /** Ids, not names. The reader is the one identity this screen can resolve. */
  actor(actorUserId: string): string {
    return actorUserId === this.me() ? 'You' : 'A reviewer';
  }

  when(iso: string): string {
    const date = new Date(iso);

    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  }
}
