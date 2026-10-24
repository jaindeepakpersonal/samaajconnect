import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError } from '@samaajconnect/shared';
import { Observable } from 'rxjs';
import { AdminApi } from '../../core/admin-api';
import {
  TrustElection,
  TrustElectionCandidate,
  TrustElectionDetail,
  TrustElectionStatus,
  TrustRole,
} from '../../core/admin.models';

/**
 * A Trust's elections, one per role at a time - set up, approve nominations,
 * move the stages, publish.
 *
 * **An election is for a role, and fills its seats.** The form offers only the
 * Trust's own roles (never Trustee, which nobody is elected to) that have no
 * election still under way; each ballot picks up to as many as the role has
 * seats, and the top that many win. Publishing seats them and returns whoever
 * held the role and lost to Trustee, which the confirmation says.
 *
 * **The flow is Volunteer Groups' election screen copied**, one button per
 * forward stage and a confirmed publish - deliberately not shared code, per
 * this repo's "copy the template, don't couple the services" rule. Nominating
 * and voting are the trustees' own, in the member portal.
 */
@Component({
  selector: 'app-trust-elections',
  imports: [FormsModule, DatePipe, RouterLink],
  template: `
    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    <div class="card">
      <div class="section-head">
        <h2>Elections</h2>
        <button class="btn" type="button" [attr.aria-expanded]="showOpen()" (click)="toggleOpenForm()">
          {{ showOpen() ? 'Cancel' : 'Set up an election' }}
        </button>
      </div>

      @if (showOpen()) {
        @if (electable().length === 0) {
          <p class="notice spaced" role="status">
            There is no role to elect. Add one with its seats on the Roles tab - Trustee is everybody's
            and is not elected, and a role already being elected is not offered twice.
          </p>
        } @else {
          <form class="spaced" (ngSubmit)="open()">
            <div class="form-grid">
              <div class="field span-all">
                <label for="election-role">Role</label>
                <select id="election-role" class="input" name="roleId" [(ngModel)]="roleId">
                  <option value="">Choose a role…</option>
                  @for (role of electable(); track role.id) {
                    <option [value]="role.id">
                      {{ role.title }} ({{ role.seats }} seat{{ role.seats === 1 ? '' : 's' }})
                    </option>
                  }
                </select>
              </div>

              <div class="field">
                <label for="nom-start">Nominations open</label>
                <input id="nom-start" class="input" type="datetime-local" name="nominationStartAt"
                  [(ngModel)]="nominationStartAt" />
              </div>
              <div class="field">
                <label for="nom-end">Nominations close</label>
                <input id="nom-end" class="input" type="datetime-local" name="nominationEndAt"
                  [(ngModel)]="nominationEndAt"
                  [attr.aria-invalid]="closesBeforeOpening(nominationStartAt, nominationEndAt) ? 'true' : null" />
              </div>
              <div class="field">
                <label for="vote-start">Voting opens</label>
                <input id="vote-start" class="input" type="datetime-local" name="votingStartAt"
                  [(ngModel)]="votingStartAt" [attr.aria-invalid]="windowsOverlap() ? 'true' : null" />
              </div>
              <div class="field">
                <label for="vote-end">Voting closes</label>
                <input id="vote-end" class="input" type="datetime-local" name="votingEndAt"
                  [(ngModel)]="votingEndAt"
                  [attr.aria-invalid]="closesBeforeOpening(votingStartAt, votingEndAt) ? 'true' : null" />
              </div>

              @if (closesBeforeOpening(nominationStartAt, nominationEndAt)) {
                <p class="notice span-all" role="alert">Nominations cannot close before they open.</p>
              }

              @if (closesBeforeOpening(votingStartAt, votingEndAt)) {
                <p class="notice span-all" role="alert">Voting cannot close before it opens.</p>
              }

              @if (windowsOverlap()) {
                <p class="notice span-all" role="alert">
                  Voting must start after nominations close, so early and late voters see the same ballot.
                </p>
              }
            </div>

            <div class="actions">
              <button class="btn" type="submit" [disabled]="busy() || !canOpen()">Set up as a draft</button>
            </div>
          </form>
        }
      }

      @if (loading()) {
        <p class="empty" role="status">Loading…</p>
      } @else if (elections().length === 0) {
        <p class="empty">No elections have been run for this Trust yet.</p>
      } @else {
        @for (election of elections(); track election.id) {
          <div class="election">
            <div class="section-head">
              <div>
                <h3>{{ election.positionTitle || 'A removed role' }}</h3>
                <p class="small">
                  {{ election.seats }} seat{{ election.seats === 1 ? '' : 's' }} · Nominations
                  {{ election.nominationStartAt | date: 'd MMM' }} — {{ election.nominationEndAt | date: 'd MMM y' }}
                  · Voting {{ election.votingStartAt | date: 'd MMM' }} — {{ election.votingEndAt | date: 'd MMM y' }}
                </p>
              </div>
              <span class="pill" [class.warn]="election.status === 'Draft'">{{ stage(election) }}</span>
            </div>

            @if (election.winnerMemberIds.length > 0) {
              <p class="notice ok">
                Elected:
                @for (winner of election.winnerMemberIds; track winner; let last = $last) {
                  <b><a [routerLink]="['/members', winner]">{{ name(winner) }}</a></b>{{ last ? '' : ', ' }}
                }
              </p>
            }

            <div class="row-actions">
              <button class="btn small alt" type="button" [attr.aria-expanded]="expandedId() === election.id"
                (click)="toggle(election)">
                {{ expandedId() === election.id ? 'Hide ballot' : 'View ballot' }}
              </button>

              @if (nextStatus(election); as next) {
                <button class="btn small" type="button" [disabled]="busy()" (click)="move(election, next)">
                  {{ moveLabel(next) }}
                </button>
              } @else if (election.status === 'Closed') {
                <button class="btn small" type="button" [disabled]="busy()"
                  [attr.aria-expanded]="confirmingPublish() === election.id"
                  (click)="confirmingPublish.set(election.id)">
                  Publish the result
                </button>
              }
            </div>

            @if (confirmingPublish() === election.id) {
              <div class="notice" role="status">
                <p>
                  This seats the {{ election.seats === 1 ? 'candidate' : election.seats + ' candidates' }}
                  with the most votes as {{ election.positionTitle }}, and returns anybody who held it
                  and did not win to Trustee. It cannot be undone or recomputed.
                </p>
                <div class="row-actions">
                  <button class="btn" type="button" [disabled]="busy()" (click)="publish(election)">
                    Confirm and publish
                  </button>
                  <button class="btn alt" type="button" (click)="confirmingPublish.set(null)">Not yet</button>
                </div>
              </div>
            }

            @if (expandedId() === election.id) {
              @if (ballot(); as shown) {
                @if (shown.candidates.length === 0) {
                  <p class="empty">
                    Nobody has been nominated yet. Active trustees nominate from the member portal while
                    nominations are open.
                  </p>
                } @else {
                  <div class="table-wrap">
                    <table>
                      <caption class="sr-only">Candidates for {{ election.positionTitle }}</caption>
                      <thead>
                        <tr><th>Member</th><th>Nominated by</th><th>Status</th><th>Votes</th><th></th></tr>
                      </thead>
                      <tbody>
                        @for (candidate of shown.candidates; track candidate.id) {
                          <tr>
                            <td><b><a [routerLink]="['/members', candidate.memberId]">{{ name(candidate.memberId) }}</a></b></td>
                            <td><a [routerLink]="['/members', candidate.nominatedBy]">{{ name(candidate.nominatedBy) }}</a></td>
                            <td>
                              <span class="pill" [class.warn]="candidate.status === 'Nominated'">
                                {{ candidate.status === 'Approved' ? 'On the ballot' : 'Waiting' }}
                              </span>
                            </td>
                            <td>{{ candidate.votes }}</td>
                            <td>
                              @if (canDecide(election)) {
                                <div class="row-actions">
                                  @if (candidate.status === 'Nominated') {
                                    <button class="btn small" type="button" [disabled]="busy()"
                                      (click)="decide(election, candidate, true)">
                                      Approve
                                    </button>
                                  }
                                  <button class="btn small alt" type="button" [disabled]="busy()"
                                    (click)="decide(election, candidate, false)">
                                    {{ candidate.status === 'Approved' ? 'Take off the ballot' : 'Turn down' }}
                                  </button>
                                </div>
                              }
                            </td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                }
              } @else {
                <p class="empty" role="status">Loading the ballot…</p>
              }
            }
          </div>
        }
      }
    </div>
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
    }

    .election {
      margin-top: var(--space-4);
      padding-top: var(--space-4);
      border-top: 1px solid var(--line);
    }

    .section-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .section-head h2,
    .section-head h3 {
      margin: 0;
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
      margin-top: var(--space-2);
    }
  `,
})
export class TrustElectionsComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly trustId = input.required<string>();

  /** Member names from the page's one directory call. */
  readonly names = input<ReadonlyMap<string, string>>(new Map());

  readonly elections = signal<readonly TrustElection[]>([]);
  readonly roles = signal<readonly TrustRole[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  readonly showOpen = signal(false);
  readonly expandedId = signal<string | null>(null);
  readonly ballot = signal<TrustElectionDetail | null>(null);
  readonly confirmingPublish = signal<string | null>(null);

  /** The Trust's own roles with no election still under way - the service refuses a second. */
  readonly electable = computed(() => {
    const running = new Set(this.elections().filter((e) => e.status !== 'Published').map((e) => e.roleId));

    return this.roles().filter((role) => role.isElected && !running.has(role.id));
  });

  roleId = '';
  nominationStartAt = '';
  nominationEndAt = '';
  votingStartAt = '';
  votingEndAt = '';

  ngOnInit(): void {
    this.loadElections();
    this.loadRoles();
  }

  name(memberId: string): string {
    return this.names().get(memberId) ?? 'A member';
  }

  private loadElections(): void {
    this.api.listTrustElections(this.trustId()).subscribe({
      next: (found) => {
        this.elections.set(found);
        this.loading.set(false);

        const open = this.expandedId();

        if (open !== null) {
          this.loadBallot(open);
        }
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }

  private loadRoles(): void {
    this.api.listTrustRoles(this.trustId()).subscribe({
      next: (found) => this.roles.set(found),
      error: (failure: unknown) => this.error.set(describeError(failure)),
    });
  }

  toggleOpenForm(): void {
    this.showOpen.set(!this.showOpen());

    if (this.showOpen()) {
      this.roleId = '';
      this.nominationStartAt = '';
      this.nominationEndAt = '';
      this.votingStartAt = '';
      this.votingEndAt = '';
    }
  }

  windowsOverlap(): boolean {
    if (!this.nominationEndAt || !this.votingStartAt) {
      return false;
    }

    return new Date(this.votingStartAt) < new Date(this.nominationEndAt);
  }

  closesBeforeOpening(open: string, close: string): boolean {
    if (!open || !close) {
      return false;
    }

    return new Date(close) <= new Date(open);
  }

  canOpen(): boolean {
    return (
      this.electable().some((role) => role.id === this.roleId) &&
      this.nominationStartAt.length > 0 &&
      this.nominationEndAt.length > 0 &&
      this.votingStartAt.length > 0 &&
      this.votingEndAt.length > 0 &&
      !this.closesBeforeOpening(this.nominationStartAt, this.nominationEndAt) &&
      !this.closesBeforeOpening(this.votingStartAt, this.votingEndAt) &&
      !this.windowsOverlap()
    );
  }

  open(): void {
    if (!this.canOpen()) {
      return;
    }

    const title = this.roles().find((role) => role.id === this.roleId)?.title ?? 'The';

    this.act(
      this.api.openTrustElection(this.trustId(), {
        roleId: this.roleId,
        nominationStartAt: new Date(this.nominationStartAt).toISOString(),
        nominationEndAt: new Date(this.nominationEndAt).toISOString(),
        votingStartAt: new Date(this.votingStartAt).toISOString(),
        votingEndAt: new Date(this.votingEndAt).toISOString(),
      }),
      `The ${title} election is set up as a draft. Nominations are not open yet.`,
      () => this.showOpen.set(false),
    );
  }

  stage(election: TrustElection): string {
    if (election.acceptsNominations) {
      return 'Nominations open';
    }

    if (election.acceptsVotes) {
      return 'Voting open';
    }

    switch (election.status) {
      case 'Draft':
        return 'Not started';
      case 'NominationsOpen':
        return 'Nominations closed';
      case 'VotingOpen':
      case 'Closed':
        return 'Voting closed';
      case 'Published':
        return 'Result published';
    }
  }

  nextStatus(election: TrustElection): TrustElectionStatus | null {
    switch (election.status) {
      case 'Draft':
        return 'NominationsOpen';
      case 'NominationsOpen':
        return 'VotingOpen';
      case 'VotingOpen':
        return 'Closed';
      default:
        return null;
    }
  }

  moveLabel(next: TrustElectionStatus): string {
    switch (next) {
      case 'NominationsOpen':
        return 'Open nominations';
      case 'VotingOpen':
        return 'Open voting';
      default:
        return 'Close voting';
    }
  }

  /** Nominations can be approved or removed only until voting opens. */
  canDecide(election: TrustElection): boolean {
    return election.status === 'Draft' || election.status === 'NominationsOpen';
  }

  toggle(election: TrustElection): void {
    if (this.expandedId() === election.id) {
      this.expandedId.set(null);
      this.ballot.set(null);
      return;
    }

    this.expandedId.set(election.id);
    this.loadBallot(election.id);
  }

  private loadBallot(electionId: string): void {
    this.ballot.set(null);

    this.api.getTrustElection(electionId).subscribe({
      next: (found) => this.ballot.set(found),
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.expandedId.set(null);
      },
    });
  }

  move(election: TrustElection, status: TrustElectionStatus): void {
    this.act(
      this.api.moveTrustElection(election.id, status),
      `${election.positionTitle} election: ${this.moveLabel(status).toLowerCase()} done.`,
    );
  }

  decide(election: TrustElection, candidate: TrustElectionCandidate, approve: boolean): void {
    this.act(
      this.api.decideTrustCandidate(election.id, candidate.id, approve),
      approve ? 'On the ballot.' : 'Off the ballot.',
    );
  }

  publish(election: TrustElection): void {
    this.confirmingPublish.set(null);

    this.act(
      this.api.publishTrustElection(election.id),
      `The ${election.positionTitle} result is announced, and the winners now hold the role.`,
    );
  }

  private act(work: Observable<unknown>, message: string, after?: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: () => {
        this.done.set(message);
        this.busy.set(false);
        after?.();
        this.loadElections();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}
